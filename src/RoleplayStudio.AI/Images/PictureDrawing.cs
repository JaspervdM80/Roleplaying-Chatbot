using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoleplayStudio.AI.Providers;
using RoleplayStudio.AI.Upkeep;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Media;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Data;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Infrastructure.Storage;

namespace RoleplayStudio.AI.Images;

/// <summary>Draws one queued picture: the utility model writes the prompt from the chat, the image model draws it, and it is stored for its owner.</summary>
public sealed class PictureDrawing(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IModelClientFactory clients,
    IImageStore store,
    PictureQueue queue,
    PictureNotifier notifier,
    TimeProvider time,
    ILogger<PictureDrawing> logger)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    // A local ComfyUI loads its models on the first picture and may be drawing something else first.
    public static readonly TimeSpan LocalTimeout = TimeSpan.FromMinutes(10);

    public static TimeSpan TimeoutFor(ProviderKind provider) => provider == ProviderKind.ComfyUI ? LocalTimeout : Timeout;

    private sealed record Plan(
        string Prompt,
        string Negative,
        string? Caption,
        int Width,
        int Height,
        long? Seed,
        IReadOnlyList<Guid> MemoryIds,
        Character? Subject,
        IReadOnlyList<Guid> ReferenceImageIds,
        IReadOnlyList<ReferenceImage> References);

    private readonly PictureReferences _references = new(store, logger);
    private readonly PictureBriefing _briefing = new(new PictureReferences(store, logger));

    public async Task<Result<Guid>> DrawAsync(PictureJob job, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateForOwnerAsync(job.OwnerId, cancellationToken);
        var opened = await PreferredModel.OpenAsync(db, ModelRole.Image, clients.CreateImageGenerator, PictureService.NoImageModel, logger, cancellationToken);
        if (opened.IsFailure)
        {
            return opened.To<Guid>();
        }

        var (profile, generator) = opened.Value;
        var traits = await generator.TraitsAsync(cancellationToken);
        var planned = job.RedrawOf is { } redrawOf
            ? await RedrawPlanAsync(db, redrawOf, traits.MaxReferenceImages, cancellationToken)
            : await PlanAsync(db, job, traits.MaxReferenceImages, cancellationToken);
        if (planned.IsFailure)
        {
            return planned.To<Guid>();
        }

        var plan = planned.Value;
        queue.Describe(job.Id, plan.Caption);
        notifier.Notify(new PictureChange(job.Id, job.SessionId, job.CharacterId, null));

        var seed = plan.Seed ?? Random.Shared.NextInt64(1, uint.MaxValue);
        var request = new ImageRequest(plan.Prompt, traits.TakesNegativePrompt ? plan.Negative : null, plan.Width, plan.Height, seed, plan.References, IsPortrait: job.SessionId is null);
        var limit = TimeoutFor(profile.Provider);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limit);
        var drawn = await ProviderErrors.TranslateAsync(profile, logger, async () =>
        {
            try
            {
                return Result.Success(await generator.GenerateAsync(request, timeout.Token));
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Model profile {ProfileId} did not draw a picture in time", profile.Id);
                return Result.Failure<GeneratedPicture>("No picture within {0} seconds", limit.TotalSeconds);
            }
        });
        if (drawn.IsFailure)
        {
            return drawn.To<Guid>();
        }

        var picture = drawn.Value;
        var image = new GeneratedImage
        {
            SessionId = job.SessionId,
            MessageId = job.MessageId,
            CharacterId = job.CharacterId,
            Prompt = plan.Prompt,
            NegativePrompt = request.NegativePrompt,
            Caption = plan.Caption,
            SourceMemoryIds = [.. plan.MemoryIds],
            ReferenceImageIds = [.. plan.ReferenceImageIds],
            Provider = profile.Provider.ToString(),
            Model = generator.ModelId,
            Seed = picture.Seed,
            ContentType = picture.ContentType,
            Width = picture.Width,
            Height = picture.Height,
            CreatedAt = time.GetUtcNow(),
        };
        // Drawn and paid for: from here the picture is kept whole even if the job is cancelled.
        var saved = await ImageService.StoreAsync(db, store, job.OwnerId, image, picture.Data, CancellationToken.None);
        if (saved.IsFailure)
        {
            logger.LogWarning("Refused to save a picture for owner {OwnerId}: {Problem}", job.OwnerId, saved.Error);
            return saved.To<Guid>();
        }

        // A character's first portrait becomes their reference; a renewed one, drawn from looks the story revealed, replaces it.
        if (saved.Value.IsPortrait && plan.Subject is { } subject && (subject.ReferenceImageId is null || job.RenewsReference))
        {
            var character = await db.Characters.SingleAsync(c => c.Id == subject.Id, CancellationToken.None);
            character.ReferenceImageId = saved.Value.Id;
            character.ImageSeed = job.RenewsReference ? picture.Seed : character.ImageSeed ?? picture.Seed;
            await db.SaveChangesAsync(CancellationToken.None);
        }

        logger.LogInformation("Drew image {ImageId} from {Provider}", saved.Value.Id, saved.Value.Provider);
        return Result.Success(saved.Value.Id);
    }

    private async Task<Result<Plan>> PlanAsync(ApplicationDbContext db, PictureJob job, int maxReferences, CancellationToken cancellationToken)
    {
        var briefed = await _briefing.BriefAsync(db, job, maxReferences, cancellationToken);
        if (briefed.IsFailure)
        {
            return briefed.To<Plan>();
        }

        var (brief, subject, memoryIds, referenceImageIds, references) = briefed.Value;
        using var utility = await UtilityModel.OpenAsync(db, clients, logger, cancellationToken);
        if (utility is null)
        {
            return Result.Failure<Plan>(PictureService.NoUtilityModel);
        }

        var reply = await utility.AskAsync(ImagePrompts.Prompt(brief), cancellationToken);
        if (reply.IsFailure)
        {
            return reply.To<Plan>();
        }

        if (ImagePrompts.Parse(reply.Value) is not { } written)
        {
            logger.LogWarning("The utility model's picture prompt for owner {OwnerId} was not readable JSON", job.OwnerId);
            return Result.Failure<Plan>("The utility model did not write a usable prompt; try again");
        }

        var (prompt, negative) = ImagePrompts.Compose(brief, written);
        var (width, height) = ImagePrompts.SizeFor(brief);
        var seed = job.RenewsReference ? null : subject?.ImageSeed;
        return new Plan(prompt, negative, written.Caption, width, height, seed, memoryIds, subject, referenceImageIds, references);
    }

    private async Task<Result<Plan>> RedrawPlanAsync(ApplicationDbContext db, Guid imageId, int maxReferences, CancellationToken cancellationToken)
    {
        var image = await db.Images.AsNoTracking().SingleOrDefaultAsync(i => i.Id == imageId, cancellationToken);
        if (image is null)
        {
            return Result.Failure<Plan>("That image no longer exists");
        }

        // The stored prompt names people by their reference image's number, so every reference has to go along, in order.
        var loaded = await _references.LoadAsync(db, image.ReferenceImageIds, cancellationToken);
        if (loaded.Count < image.ReferenceImageIds.Distinct().Count())
        {
            return Result.Failure<Plan>("A portrait this picture was drawn after is gone; picture the moment again instead");
        }

        if (image.ReferenceImageIds.Count > maxReferences)
        {
            return Result.Failure<Plan>("The image model takes fewer reference portraits than this picture was drawn after; picture the moment again instead");
        }

        var subject = image.CharacterId is { } characterId ? await db.Characters.AsNoTracking().SingleOrDefaultAsync(c => c.Id == characterId, cancellationToken) : null;

        // Drawn again with a new seed: the stored seed would only give back the same picture.
        return new Plan(
            image.Prompt,
            image.NegativePrompt ?? string.Empty,
            image.Caption,
            image.Width,
            image.Height,
            null,
            image.SourceMemoryIds,
            subject,
            image.ReferenceImageIds,
            [.. image.ReferenceImageIds.Select(id => loaded[id])]);
    }
}
