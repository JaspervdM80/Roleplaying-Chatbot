using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoleplayStudio.Domain;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Data;

namespace RoleplayStudio.Infrastructure.Services;

public sealed record ChatListCharacter(Guid Id, string Name);

/// <summary>A chat on the chats list; <see cref="LastLine"/> is the start of the newest message, or null before the first.</summary>
public sealed record ChatListItem(
    Guid Id, string ChatbotName, string ScenarioTitle, string PersonaName, IReadOnlyList<ChatListCharacter> Characters, string? LastLine, DateTimeOffset LastActivityAt);

public sealed class ChatSessionService(IDbContextFactory<ApplicationDbContext> dbFactory, ICurrentUser currentUser, TimeProvider time, ILogger<ChatSessionService> logger)
{
    private const int LastLineLength = 160;

    public Task<Result<IReadOnlyList<ChatListItem>>> ListAsync(CancellationToken cancellationToken = default) =>
        ServiceOperation.RunOwnerAsync<IReadOnlyList<ChatListItem>>(currentUser, logger, "list chats", cancellationToken, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId, cancellationToken);
            var rows = await db.ChatSessions
                .OrderByDescending(s => s.LastActivityAt)
                .Select(s => new
                {
                    s.Id,
                    ChatbotName = s.Scenario.Chatbot.Name,
                    ScenarioTitle = s.Scenario.Title,
                    PersonaName = s.Persona.Name,
                    Characters = s.CharacterStates.OrderBy(c => c.Character.Name).Select(c => new ChatListCharacter(c.CharacterId, c.Character.Name)).ToList(),
                    Last = s.Messages.OrderByDescending(m => m.Sequence).Select(m => new { m.SpeakerName, Start = m.Content.Substring(0, LastLineLength) }).FirstOrDefault(),
                    s.LastActivityAt,
                })
                .AsSplitQuery()
                .ToListAsync(cancellationToken);
            var chats = rows
                .Select(r => new ChatListItem(r.Id, r.ChatbotName, r.ScenarioTitle, r.PersonaName, r.Characters, r.Last is null ? null : $"{r.Last.SpeakerName}: {r.Last.Start}", r.LastActivityAt))
                .ToList();
            logger.LogDebug("Listed {ChatCount} chats", chats.Count);
            return Result.Success<IReadOnlyList<ChatListItem>>(chats);
        });

    /// <summary>The session with its scenario, chatbot, persona, character states and every message in order.</summary>
    public Task<Result<ChatSession>> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "load the chat", cancellationToken, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId, cancellationToken);
            var session = await db.ChatSessions
                .WithScenarioAndPersona()
                .Include(s => s.Messages.OrderBy(m => m.Sequence))
                .AsNoTracking()
                .AsSplitQuery()
                .SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
            return session ?? NotFound(id).To<ChatSession>();
        });

    /// <summary>The session as <see cref="GetAsync"/> loads it but without its messages, for refreshing the scene.</summary>
    public Task<Result<ChatSession>> GetSceneAsync(Guid id, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "load the scene", cancellationToken, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId, cancellationToken);
            var session = await db.ChatSessions
                .WithScenarioAndPersona()
                .AsNoTracking()
                .AsSplitQuery()
                .SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
            return session ?? NotFound(id).To<ChatSession>();
        });

    public Task<Result<ChatSession>> StartAsync(Guid chatbotId, Guid scenarioId, Guid personaId) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "start the chat", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var chatbot = await db.Chatbots.WithCastAndScenarios().AsNoTracking().AsSplitQuery().SingleOrDefaultAsync(c => c.Id == chatbotId);
            var scenario = chatbot?.Scenarios.Find(s => s.Id == scenarioId);
            if (chatbot is null || scenario is null)
            {
                logger.LogWarning("Scenario {ScenarioId} of chatbot {ChatbotId} was not found", scenarioId, chatbotId);
                return Result.Failure<ChatSession>("That scenario no longer exists");
            }

            var persona = await db.Personas.AsNoTracking().SingleOrDefaultAsync(p => p.Id == personaId);
            if (persona is null)
            {
                logger.LogWarning("Persona {PersonaId} was not found", personaId);
                return Result.Failure<ChatSession>("That persona no longer exists");
            }

            var modelId = chatbot.DefaultChatModelProfileId
                ?? await db.ModelProfiles.Where(p => p.Role == ModelRole.Chat && p.IsDefault).Select(p => (Guid?)p.Id).FirstOrDefaultAsync();
            var cast = chatbot.Cast.Select(m => m.Character).ToList();
            var session = ChatSession.Start(scenario, persona, cast, modelId, time.GetUtcNow());

            db.ChatSessions.Add(session);
            await db.SaveChangesAsync();
            logger.LogInformation("Started chat {SessionId} from scenario {ScenarioId}", session.Id, scenarioId);
            return session;
        });

    public Task<Result> SetModelAsync(Guid id, Guid? profileId) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "change the chat model", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var session = await db.ChatSessions.SingleOrDefaultAsync(s => s.Id == id);
            if (session is null)
            {
                return NotFound(id);
            }

            if (profileId is { } wanted && !await db.ModelProfiles.AnyAsync(p => p.Id == wanted && p.Role == ModelRole.Chat))
            {
                logger.LogWarning("Model profile {ProfileId} is not one of the caller's chat models", wanted);
                return Result.Failure("That chat model no longer exists");
            }

            session.ChatModelProfileId = profileId;
            await db.SaveChangesAsync();
            logger.LogInformation("Chat {SessionId} now uses model profile {ProfileId}", id, profileId);
            return Result.Success();
        });

    /// <summary>The cast of the chat's chatbot, by name, whether or not they are in the scene.</summary>
    public Task<Result<IReadOnlyList<Character>>> ListCastAsync(Guid id, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunOwnerAsync<IReadOnlyList<Character>>(currentUser, logger, "load the cast", cancellationToken, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId, cancellationToken);
            var chatbotId = await db.ChatSessions.Where(s => s.Id == id).Select(s => (Guid?)s.Scenario.ChatbotId).SingleOrDefaultAsync(cancellationToken);
            if (chatbotId is null)
            {
                return NotFound(id).To<IReadOnlyList<Character>>();
            }

            var cast = await db.Chatbots
                .CastOf(chatbotId.Value)
                .OrderBy(c => c.Name)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            logger.LogDebug("Listed {CastCount} cast members for chat {SessionId}", cast.Count, id);
            return Result.Success<IReadOnlyList<Character>>(cast);
        });

    /// <summary>Puts a member of the chatbot's cast in the scene beside whoever is there already.</summary>
    public Task<Result> BringInAsync(Guid id, Guid characterId) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "bring them in", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var session = await db.ChatSessions.Include(s => s.Scenario).Include(s => s.CharacterStates).SingleOrDefaultAsync(s => s.Id == id);
            if (session is null)
            {
                return NotFound(id);
            }

            var character = await db.Chatbots.CastOf(session.Scenario.ChatbotId).SingleOrDefaultAsync(c => c.Id == characterId);
            if (character is null)
            {
                logger.LogWarning("Character {CharacterId} is not in the cast of chat {SessionId}", characterId, id);
                return Result.Failure("They are no longer in this chatbot's cast");
            }

            var before = session.CharacterStates.ToList();
            session.BringIn(character, time.GetUtcNow());
            // Added to its DbSet: see known_issues, "A child row added to a loaded parent's collection".
            db.CharacterStates.AddRange(session.CharacterStates.Except(before));
            await db.SaveChangesAsync();
            logger.LogInformation("Brought character {CharacterId} into chat {SessionId}", characterId, id);
            return Result.Success();
        });

    public Task<Result> MoveSceneAsync(Guid id, string location) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "move the scene", CancellationToken.None, async ownerId =>
        {
            if (TextFields.Clean(location) is not { } place)
            {
                return Result.Failure("Say where the scene moves to");
            }

            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var session = await db.ChatSessions.SingleOrDefaultAsync(s => s.Id == id);
            if (session is null)
            {
                return NotFound(id);
            }

            session.Scene.Location = place;
            await db.SaveChangesAsync();
            logger.LogInformation("Moved the scene of chat {SessionId}", id);
            return Result.Success();
        });

    public Task<Result> CorrectSceneAsync(Guid id, SceneFacts opened, SceneFacts corrected) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "change the scene", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var session = await db.ChatSessions.SingleOrDefaultAsync(s => s.Id == id);
            if (session is null)
            {
                return NotFound(id);
            }

            session.CorrectScene(opened, corrected);
            await db.SaveChangesAsync();
            logger.LogInformation("Corrected the scene of chat {SessionId}", id);
            return Result.Success();
        });

    public Task<Result> CorrectPersonaOutfitAsync(Guid id, string? opened, string? corrected) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "change what you wear", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var session = await db.ChatSessions.Include(s => s.Persona).SingleOrDefaultAsync(s => s.Id == id);
            if (session is null)
            {
                return NotFound(id);
            }

            session.CorrectPersonaOutfit(session.Persona, opened, corrected);
            await db.SaveChangesAsync();
            logger.LogInformation("Corrected the persona's outfit in chat {SessionId}", id);
            return Result.Success();
        });

    public Task<Result> CorrectCharacterAsync(Guid id, CharacterSnapshot opened, CharacterSnapshot corrected) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "change how they are", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var session = await db.ChatSessions.Include(s => s.CharacterStates).SingleOrDefaultAsync(s => s.Id == id);
            if (session is null)
            {
                return NotFound(id);
            }

            if (!session.CorrectCharacter(opened, corrected, time.GetUtcNow()))
            {
                logger.LogWarning("Character {CharacterId} has not been met in chat {SessionId}", corrected.CharacterId, id);
                return Result.Failure("They are no longer in this chat");
            }

            await db.SaveChangesAsync();
            logger.LogInformation("Corrected character {CharacterId} in chat {SessionId}", corrected.CharacterId, id);
            return Result.Success();
        });

    public Task<Result<Message>> AddUserMessageAsync(Guid id, string text) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "send the message", CancellationToken.None, async ownerId =>
        {
            if (TextFields.Clean(text) is not { } content)
            {
                return Result.Failure<Message>("Write something first");
            }

            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var session = await db.ChatSessions.Include(s => s.Persona).SingleOrDefaultAsync(s => s.Id == id);
            if (session is null)
            {
                return NotFound(id).To<Message>();
            }

            return (await AppendAsync(db, session, [new NewLine(MessageRole.User, null, session.Persona.Name, content)]))[0];
        });

    /// <summary>
    /// Saves a model reply as one message per segment <paramref name="split"/> makes of it for the characters present.
    /// A segment naming anyone not present is narration.
    /// </summary>
    public Task<Result<IReadOnlyList<Message>>> AddReplyAsync(Guid id, Func<IReadOnlyList<Character>, IReadOnlyList<ReplySegment>> split) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "save the reply", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var session = await db.ChatSessions.Include(s => s.CharacterStates).ThenInclude(s => s.Character).SingleOrDefaultAsync(s => s.Id == id);
            if (session is null)
            {
                return NotFound(id).To<IReadOnlyList<Message>>();
            }

            var present = session.PresentCharacters();
            var lines = split(present)
                .Select(segment => (Speaker: present.FirstOrDefault(c => c.Id == segment.CharacterId), Content: TextFields.Clean(segment.Text)))
                .Where(line => line.Content is not null)
                .Select(line => line.Speaker is { } speaker
                    ? new NewLine(MessageRole.Character, speaker.Id, speaker.Name, line.Content!)
                    : new NewLine(MessageRole.Narrator, null, ChatSession.NarratorName, line.Content!))
                .ToList();
            if (lines.Count == 0)
            {
                return Result.Failure<IReadOnlyList<Message>>("The reply was empty");
            }

            return Result.Success(await AppendAsync(db, session, lines));
        });

    public Task<Result> DeleteAsync(Guid id) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "delete the chat", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var session = await db.ChatSessions.SingleOrDefaultAsync(s => s.Id == id);
            if (session is null)
            {
                return NotFound(id);
            }

            db.ChatSessions.Remove(session);
            await db.SaveChangesAsync();
            logger.LogInformation("Deleted chat {SessionId}", id);
            return Result.Success();
        });

    private async Task<IReadOnlyList<Message>> AppendAsync(ApplicationDbContext db, ChatSession session, IReadOnlyList<NewLine> lines)
    {
        var now = time.GetUtcNow();
        var last = await db.Messages.Where(m => m.SessionId == session.Id).MaxAsync(m => (long?)m.Sequence) ?? 0;
        var messages = lines
            .Select((line, i) => new Message
            {
                SessionId = session.Id,
                Sequence = last + 1 + i,
                Role = line.Role,
                SpeakerCharacterId = line.SpeakerId,
                SpeakerName = line.SpeakerName,
                Content = line.Content,
                CreatedAt = now,
            })
            .ToList();
        db.Messages.AddRange(messages);
        session.LastActivityAt = now;
        await db.SaveChangesAsync();
        logger.LogInformation("Added {Count} message(s) from {Sequence} to chat {SessionId}", messages.Count, messages[0].Sequence, session.Id);
        return messages;
    }

    private Result NotFound(Guid id) => ServiceOperation.NotFound(logger, id, "That chat no longer exists");

    private sealed record NewLine(MessageRole Role, Guid? SpeakerId, string SpeakerName, string Content);
}
