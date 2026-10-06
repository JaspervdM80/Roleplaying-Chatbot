using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoleplayStudio.AI.Providers;
using RoleplayStudio.AI.Upkeep;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Infrastructure.Data;

namespace RoleplayStudio.AI.Scene;

/// <summary>After a turn: reads the new messages for where the scene is, who is in it and what they wear, and meets newcomers.</summary>
public sealed class SceneUpkeep(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IChatClientFactory clients,
    SceneNotifier notifier,
    TimeProvider time,
    ILogger<SceneUpkeep> logger)
{
    public async Task RunAsync(UpkeepJob job, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateForOwnerAsync(job.OwnerId, cancellationToken);
        var session = await db.ChatSessions
            .Include(s => s.Persona)
            .Include(s => s.Scenario)
            .Include(s => s.CharacterStates).ThenInclude(s => s.Character)
            .AsSplitQuery()
            .SingleOrDefaultAsync(s => s.Id == job.SessionId, cancellationToken);
        if (session is null)
        {
            logger.LogDebug("Chat {SessionId} is gone; skipped scene tracking", job.SessionId);
            return;
        }

        var messages = await db.Messages
            .Where(m => m.SessionId == session.Id && m.Sequence > session.Scene.TrackedUpToSequence)
            .OrderByDescending(m => m.Sequence)
            .Take(SceneTracking.MaxMessages)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        if (messages.Count == 0)
        {
            return;
        }

        using var utility = await UtilityModel.OpenAsync(db, clients, logger, cancellationToken);
        if (utility is null)
        {
            return;
        }

        var chatbotId = session.Scenario.ChatbotId;
        var cast = await db.Chatbots.Where(c => c.Id == chatbotId).SelectMany(c => c.Cast.Select(m => m.Character)).ToListAsync(cancellationToken);
        var known = new SceneCast(session.CharacterStates, cast);
        var reply = await utility.AskAsync(SceneTracking.Prompt(session, session.Persona.Name, known, messages), cancellationToken);
        if (reply.IsFailure)
        {
            return;
        }

        var lastSequence = messages.Max(m => m.Sequence);
        var update = SceneTracking.Parse(reply.Value);
        var met = 0;
        if (update is null)
        {
            // Skipped rather than retried, so a model that cannot write the JSON does not pay for the same messages every turn.
            logger.LogWarning("The utility model's scene for chat {SessionId} was not readable JSON; skipped up to message {Sequence}", session.Id, lastSequence);
        }
        else
        {
            var before = session.CharacterStates.ToList();
            var newcomers = SceneTracking.Apply(session, known, update, session.Persona.Name, time.GetUtcNow());

            // Found only through the collection, a state with a client-made id would be taken for an existing row and updated.
            db.CharacterStates.AddRange(session.CharacterStates.Except(before));
            foreach (var (character, role) in newcomers)
            {
                db.Characters.Add(character);
                db.Add(new ChatbotCharacter { ChatbotId = chatbotId, CharacterId = character.Id, Role = role });
            }

            met = newcomers.Count;
        }

        session.Scene.TrackedUpToSequence = lastSequence;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Tracked the scene of chat {SessionId} up to message {Sequence}; met {NewcomerCount} newcomers", session.Id, lastSequence, met);
        notifier.Notify(session.Id);
    }
}
