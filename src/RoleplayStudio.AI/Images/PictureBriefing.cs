using Microsoft.EntityFrameworkCore;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Domain.Memory;
using RoleplayStudio.Infrastructure.Data;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.AI.Images;

internal sealed record Briefing(PictureBrief Brief, Character? Subject, IReadOnlyList<Guid> MemoryIds, IReadOnlyList<Guid> ReferenceImageIds, IReadOnlyList<ReferenceImage> References);

/// <summary>Gathers what a new picture shows: the people with their references, and for a chat the scene, the moment and related memories.</summary>
internal sealed class PictureBriefing(PictureReferences references)
{
    public Task<Result<Briefing>> BriefAsync(ApplicationDbContext db, PictureJob job, int maxReferences, CancellationToken cancellationToken) =>
        job.SessionId is { } sessionId
            ? ChatBriefAsync(db, sessionId, job, maxReferences, cancellationToken)
            : PortraitBriefAsync(db, job, maxReferences, cancellationToken);

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
            states = session.PresentStates();
        }

        var numbering = new ReferenceNumbering(await references.LoadAsync(db, states.Select(s => s.Character.ReferenceImageId).OfType<Guid>().ToList(), cancellationToken), maxReferences);
        var subject = job.CharacterId is null ? null : states[0].Character;
        List<PicturedPerson> people = [.. states.Select(s => PicturedPerson.Of(s.Character, s, numbering.Number(s.Character)))];
        if (subject is null)
        {
            people.Add(PicturedPerson.Of(session.Persona, session.Scene.PersonaWears(session.Persona)));
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
        var numbering = new ReferenceNumbering(await references.LoadAsync(db, candidates, cancellationToken), maxReferences);
        var brief = new PictureBrief([PicturedPerson.Of(character, null, numbering.Number(character))], character.Name, IsPortrait: true);
        return new Briefing(brief, character, [], numbering.Ids, numbering.Images);
    }
}
