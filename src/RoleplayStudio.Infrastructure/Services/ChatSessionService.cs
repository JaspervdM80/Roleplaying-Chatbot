using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoleplayStudio.Domain;
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

            return await AppendAsync(db, session, MessageRole.User, null, session.Persona.Name, content);
        });

    /// <summary>Saves a model reply, attributed to the one character present or else to the narrator.</summary>
    public Task<Result<Message>> AddReplyAsync(Guid id, string text) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "save the reply", CancellationToken.None, async ownerId =>
        {
            if (TextFields.Clean(text) is not { } content)
            {
                return Result.Failure<Message>("The reply was empty");
            }

            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var session = await db.ChatSessions.Include(s => s.CharacterStates).ThenInclude(s => s.Character).SingleOrDefaultAsync(s => s.Id == id);
            if (session is null)
            {
                return NotFound(id).To<Message>();
            }

            var (role, speakerId, speakerName) = ChatSession.ReplySpeaker(session.PresentCharacters(session.CharacterStates));
            return await AppendAsync(db, session, role, speakerId, speakerName, content);
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

    private async Task<Result<Message>> AppendAsync(ApplicationDbContext db, ChatSession session, MessageRole role, Guid? speakerId, string speakerName, string content)
    {
        var now = time.GetUtcNow();
        var last = await db.Messages.Where(m => m.SessionId == session.Id).MaxAsync(m => (long?)m.Sequence) ?? 0;
        var message = new Message
        {
            SessionId = session.Id,
            Sequence = last + 1,
            Role = role,
            SpeakerCharacterId = speakerId,
            SpeakerName = speakerName,
            Content = content,
            CreatedAt = now,
        };
        db.Messages.Add(message);
        session.LastActivityAt = now;
        await db.SaveChangesAsync();
        logger.LogInformation("Added {Role} message {Sequence} to chat {SessionId}", role, message.Sequence, session.Id);
        return message;
    }

    private Result NotFound(Guid id)
    {
        logger.LogWarning("Chat {SessionId} was not found", id);
        return Result.Failure("That chat no longer exists");
    }
}
