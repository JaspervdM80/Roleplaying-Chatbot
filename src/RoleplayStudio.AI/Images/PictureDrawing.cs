using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoleplayStudio.AI.Providers;
using RoleplayStudio.AI.Upkeep;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Media;
using RoleplayStudio.Domain.Memory;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Data;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Infrastructure.Storage;

namespace RoleplayStudio.AI.Images;

/// <summary>Draws one queued picture: the utility model writes the prompt from the chat, the image model draws it, and it is stored for its owner.</summary>
public sealed class PictureDrawing(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IChatClientFactory clients,
    IImageStore store,
    PictureQueue queue,
    PictureNotifier notifier,
    TimeProvider time,
    ILogger<PictureDrawing> logger)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    private sealed record Plan(string Prompt, string Negative, string? Caption, int Width, int Height, long? Seed, IReadOnlyList<Guid> MemoryIds, Character? Subject);

    public async Task<Result<Guid>> DrawAsync(PictureJob job, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateForOwnerAsync(job.OwnerId, cancellationToken);
        var profile = await db.ModelProfiles.PreferredFor(ModelRole.Image).AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            logger.LogWarning("No image model; skipped a picture for owner {OwnerId}", job.OwnerId);
            return Result.Failure<Guid>(PictureService.NoImageModel);
        }

        var created = clients.CreateImageGenerator(profile);
        if (created.IsFailure)
        {
            logger.LogWarning("Model profile {ProfileId} cannot be turned into an image generator", profile.Id);
            return created.To<Guid>();
        }

        var planned = job.RedrawOf is { } redrawOf ? await RedrawPlanAsync(db, redrawOf, cancellationToken) : await PlanAsync(db, job, cancellationToken);
        if (planned.IsFailure)
        {
            return planned.To<Guid>();
        }

        var plan = planned.Value;
        queue.Describe(job.Id, plan.Caption);
        notifier.Notify(new PictureChange(job.Id, job.SessionId, job.CharacterId, null));

        var reference = profile.AcceptsReferenceImage ? await ReferenceAsync(db, plan.Subject, cancellationToken) : null;
        var seed = plan.Seed ?? Random.Shared.NextInt64(1, uint.MaxValue);
        var request = new ImageRequest(plan.Prompt, plan.Negative, plan.Width, plan.Height, seed, reference);
        var generator = created.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        var drawn = await ProviderErrors.TranslateAsync(profile, logger, async () =>
        {
            try
            {
                return Result.Success(await generator.GenerateAsync(request, timeout.Token));
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Model profile {ProfileId} did not draw a picture in time", profile.Id);
                return Result.Failure<GeneratedPicture>("No picture within {0} seconds", Timeout.TotalSeconds);
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
            NegativePrompt = plan.Negative,
            Caption = plan.Caption,
            SourceMemoryIds = [.. plan.MemoryIds],
            Provider = profile.Provider.ToString(),
            Model = generator.ModelId,
            Seed = picture.Seed,
            ContentType = picture.ContentType,
            Width = plan.Width,
            Height = plan.Height,
            CreatedAt = time.GetUtcNow(),
        };
        // Drawn and paid for: from here the picture is kept whole even if the job is cancelled.
        var saved = await ImageService.StoreAsync(db, store, job.OwnerId, image, picture.Data, CancellationToken.None);
        if (saved.IsFailure)
        {
            logger.LogWarning("Refused to save a picture for owner {OwnerId}: {Problem}", job.OwnerId, saved.Error);
            return saved.To<Guid>();
        }

        // The first portrait a character gets is the one later pictures are drawn after, until the user picks another.
        if (saved.Value.IsPortrait && plan.Subject is { ReferenceImageId: null } subject)
        {
            var character = await db.Characters.SingleAsync(c => c.Id == subject.Id, CancellationToken.None);
            character.ReferenceImageId = saved.Value.Id;
            character.ImageSeed ??= picture.Seed;
            await db.SaveChangesAsync(CancellationToken.None);
        }

        logger.LogInformation("Drew image {ImageId} from {Provider}", saved.Value.Id, saved.Value.Provider);
        return Result.Success(saved.Value.Id);
    }

    private async Task<Result<Plan>> PlanAsync(ApplicationDbContext db, PictureJob job, CancellationToken cancellationToken)
    {
        var briefed = job.SessionId is { } sessionId ? await ChatBriefAsync(db, sessionId, job, cancellationToken) : await PortraitBriefAsync(db, job, cancellationToken);
        if (briefed.IsFailure)
        {
            return briefed.To<Plan>();
        }

        var (brief, subject, memoryIds) = briefed.Value;
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
        return new Plan(prompt, negative, written.Caption, width, height, subject?.ImageSeed, memoryIds, subject);
    }

    private async Task<Result<(PictureBrief, Character?, IReadOnlyList<Guid>)>> ChatBriefAsync(ApplicationDbContext db, Guid sessionId, PictureJob job, CancellationToken cancellationToken)
    {
        var session = await db.ChatSessions
            .WithScenarioAndPersona()
            .AsNoTracking()
            .AsSplitQuery()
            .SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null)
        {
            return Result.Failure<(PictureBrief, Character?, IReadOnlyList<Guid>)>("That chat no longer exists");
        }

        var upTo = job.MessageId is { } messageId
            ? await db.Messages.Where(m => m.SessionId == sessionId && m.Id == messageId).Select(m => (long?)m.Sequence).SingleOrDefaultAsync(cancellationToken)
            : long.MaxValue;
        if (upTo is null)
        {
            return Result.Failure<(PictureBrief, Character?, IReadOnlyList<Guid>)>("That message no longer exists");
        }

        var moment = await db.Messages
            .Where(m => m.SessionId == sessionId && m.Sequence <= upTo)
            .OrderByDescending(m => m.Sequence)
            .Take(ImagePrompts.MaxMessages)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        Character? subject = null;
        List<PicturedPerson> people;
        if (job.CharacterId is { } characterId)
        {
            var state = session.CharacterStates.FirstOrDefault(s => s.CharacterId == characterId);
            if (state is null)
            {
                return Result.Failure<(PictureBrief, Character?, IReadOnlyList<Guid>)>("That character has not been met in this chat");
            }

            subject = state.Character;
            people = [PicturedPerson.Of(state.Character, state)];
        }
        else
        {
            people = [.. session.PresentStates(session.CharacterStates).Select(s => PicturedPerson.Of(s.Character, s)), PicturedPerson.Of(session.Persona)];
        }

        var pictured = job.CharacterId is { } focusId ? [focusId] : session.Scene.PresentCharacterIds;
        var memories = await db.Memories
            .Where(m => m.SessionId == sessionId && m.RelatedCharacterIds.Any(id => pictured.Contains(id)))
            .OrderByDescending(m => m.Type == MemoryType.Appearance)
            .ThenByDescending(m => m.IsPinned)
            .ThenByDescending(m => m.Importance)
            .Take(ImagePrompts.MaxMemories)
            .Select(m => new { m.Id, m.Text })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var brief = new PictureBrief(
            people,
            subject?.Name,
            IsPortrait: false,
            session.Scene.Location,
            session.Scene.TimeOfDay,
            session.Scene.Mood,
            session.Scenario.Chatbot.WorldDescription,
            session.Scenario.Chatbot.ImageStylePreset,
            ImagePrompts.Moment(moment),
            memories.Select(m => m.Text).ToList());
        return (brief, subject, memories.Select(m => m.Id).ToList());
    }

    private static async Task<Result<(PictureBrief, Character?, IReadOnlyList<Guid>)>> PortraitBriefAsync(ApplicationDbContext db, PictureJob job, CancellationToken cancellationToken)
    {
        var character = await db.Characters.AsNoTracking().SingleOrDefaultAsync(c => c.Id == job.CharacterId, cancellationToken);
        if (character is null)
        {
            return Result.Failure<(PictureBrief, Character?, IReadOnlyList<Guid>)>("That character no longer exists");
        }

        var brief = new PictureBrief([PicturedPerson.Of(character, null)], character.Name, IsPortrait: true);
        return (brief, character, (IReadOnlyList<Guid>)[]);
    }

    private static async Task<Result<Plan>> RedrawPlanAsync(ApplicationDbContext db, Guid imageId, CancellationToken cancellationToken)
    {
        var image = await db.Images.AsNoTracking().SingleOrDefaultAsync(i => i.Id == imageId, cancellationToken);
        if (image is null)
        {
            return Result.Failure<Plan>("That image no longer exists");
        }

        var subject = image.CharacterId is { } characterId ? await db.Characters.AsNoTracking().SingleOrDefaultAsync(c => c.Id == characterId, cancellationToken) : null;

        // Drawn again with a new seed: the stored seed would only give back the same picture.
        return new Plan(image.Prompt, string.Empty, image.Caption, image.Width, image.Height, null, image.SourceMemoryIds, subject);
    }

    private async Task<ReferenceImage?> ReferenceAsync(ApplicationDbContext db, Character? subject, CancellationToken cancellationToken)
    {
        if (subject?.ReferenceImageId is not { } referenceId)
        {
            return null;
        }

        var reference = await db.Images.AsNoTracking().Where(i => i.Id == referenceId).Select(i => new { i.StoragePath, i.ContentType }).SingleOrDefaultAsync(cancellationToken);
        if (reference is null || store.OpenRead(reference.StoragePath) is not { } content)
        {
            logger.LogWarning("The reference portrait of character {CharacterId} is gone; drew without it", subject.Id);
            return null;
        }

        await using (content)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            return new ReferenceImage(copy.ToArray(), reference.ContentType);
        }
    }
}
