using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoleplayStudio.AI.Providers;
using RoleplayStudio.AI.Upkeep;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;
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

    private sealed record Briefing(PictureBrief Brief, Character? Subject, IReadOnlyList<Guid> MemoryIds, IReadOnlyList<Guid> ReferenceImageIds, IReadOnlyList<ReferenceImage> References);

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

        var generator = created.Value;
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
        var request = new ImageRequest(plan.Prompt, traits.TakesNegativePrompt ? plan.Negative : null, plan.Width, plan.Height, seed, plan.References);
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
        var briefed = job.SessionId is { } sessionId
            ? await ChatBriefAsync(db, sessionId, job, maxReferences, cancellationToken)
            : await PortraitBriefAsync(db, job, maxReferences, cancellationToken);
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

    private async Task<Result<Briefing>> ChatBriefAsync(ApplicationDbContext db, Guid sessionId, PictureJob job, int maxReferences, CancellationToken cancellationToken)
    {
        var session = await db.ChatSessions
            .WithScenarioAndPersona()
            .AsNoTracking()
            .AsSplitQuery()
            .SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null)
        {
            return Result.Failure<Briefing>("That chat no longer exists");
        }

        var upTo = job.MessageId is { } messageId
            ? await db.Messages.Where(m => m.SessionId == sessionId && m.Id == messageId).Select(m => (long?)m.Sequence).SingleOrDefaultAsync(cancellationToken)
            : long.MaxValue;
        if (upTo is null)
        {
            return Result.Failure<Briefing>("That message no longer exists");
        }

        var moment = await db.Messages
            .Where(m => m.SessionId == sessionId && m.Sequence <= upTo)
            .OrderByDescending(m => m.Sequence)
            .Take(ImagePrompts.MaxMessages)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        IReadOnlyList<CharacterState> states;
        if (job.CharacterId is { } characterId)
        {
            if (session.CharacterStates.FirstOrDefault(s => s.CharacterId == characterId) is not { } state)
            {
                return Result.Failure<Briefing>("That character has not been met in this chat");
            }

            states = [state];
        }
        else
        {
            states = session.PresentStates(session.CharacterStates);
        }

        var numbering = new ReferenceNumbering(await LoadReferencesAsync(db, states.Select(s => s.Character.ReferenceImageId).OfType<Guid>().ToList(), cancellationToken), maxReferences);
        var subject = job.CharacterId is null ? null : states[0].Character;
        List<PicturedPerson> people = [.. states.Select(s => PicturedPerson.Of(s.Character, s, numbering.Number(s.Character)))];
        if (subject is null)
        {
            people.Add(PicturedPerson.Of(session.Persona));
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
        return new Briefing(brief, subject, memories.Select(m => m.Id).ToList(), numbering.Ids, numbering.Images);
    }

    private async Task<Result<Briefing>> PortraitBriefAsync(ApplicationDbContext db, PictureJob job, int maxReferences, CancellationToken cancellationToken)
    {
        var character = await db.Characters.AsNoTracking().SingleOrDefaultAsync(c => c.Id == job.CharacterId, cancellationToken);
        if (character is null)
        {
            return Result.Failure<Briefing>("That character no longer exists");
        }

        // A renewed portrait is drawn from the looks just learned, not after the old picture it replaces.
        List<Guid> candidates = job.RenewsReference || character.ReferenceImageId is not { } referenceId ? [] : [referenceId];
        var numbering = new ReferenceNumbering(await LoadReferencesAsync(db, candidates, cancellationToken), maxReferences);
        var brief = new PictureBrief([PicturedPerson.Of(character, null, numbering.Number(character))], character.Name, IsPortrait: true);
        return new Briefing(brief, character, [], numbering.Ids, numbering.Images);
    }

    private async Task<Result<Plan>> RedrawPlanAsync(ApplicationDbContext db, Guid imageId, int maxReferences, CancellationToken cancellationToken)
    {
        var image = await db.Images.AsNoTracking().SingleOrDefaultAsync(i => i.Id == imageId, cancellationToken);
        if (image is null)
        {
            return Result.Failure<Plan>("That image no longer exists");
        }

        // The stored prompt names people by their reference image's number, so every reference has to go along, in order.
        var loaded = await LoadReferencesAsync(db, image.ReferenceImageIds, cancellationToken);
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

    /// <summary>The reference images that can still be read, by image id; one that cannot is logged and left out.</summary>
    private async Task<IReadOnlyDictionary<Guid, ReferenceImage>> LoadReferencesAsync(ApplicationDbContext db, IReadOnlyList<Guid> imageIds, CancellationToken cancellationToken)
    {
        var loaded = new Dictionary<Guid, ReferenceImage>();
        if (imageIds.Count == 0)
        {
            return loaded;
        }

        var stored = await db.Images.AsNoTracking().Where(i => imageIds.Contains(i.Id)).Select(i => new { i.Id, i.StoragePath, i.ContentType }).ToListAsync(cancellationToken);
        foreach (var imageId in imageIds.Distinct())
        {
            if (stored.FirstOrDefault(i => i.Id == imageId) is not { } reference || store.OpenRead(reference.StoragePath) is not { } content)
            {
                logger.LogWarning("Reference image {ImageId} is gone; drawing without it", imageId);
                continue;
            }

            await using (content)
            {
                using var copy = new MemoryStream();
                await content.CopyToAsync(copy, cancellationToken);
                loaded[imageId] = new ReferenceImage(copy.ToArray(), reference.ContentType);
            }
        }

        return loaded;
    }

    /// <summary>Numbers, from 1, the people whose reference portrait loaded, while the model takes more.</summary>
    private sealed class ReferenceNumbering(IReadOnlyDictionary<Guid, ReferenceImage> loaded, int max)
    {
        private readonly List<Guid> _ids = [];

        public IReadOnlyList<Guid> Ids => _ids;

        public IReadOnlyList<ReferenceImage> Images => [.. _ids.Select(id => loaded[id])];

        public int? Number(Character character)
        {
            if (character.ReferenceImageId is not { } id || !loaded.ContainsKey(id))
            {
                return null;
            }

            if (!_ids.Contains(id))
            {
                if (_ids.Count >= max)
                {
                    return null;
                }

                _ids.Add(id);
            }

            return _ids.IndexOf(id) + 1;
        }
    }
}
