using Microsoft.Extensions.Logging;
using RoleplayStudio.AI.Memory;
using RoleplayStudio.AI.Providers;
using RoleplayStudio.AI.Upkeep;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Domain.Memory;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.AI.Chat;

public sealed class ChatTurnService(
    ChatSessionService sessions,
    ChatHistoryService history,
    ModelProfileService profiles,
    IChatClientFactory clients,
    MemoryRecall recall,
    UpkeepQueue upkeepQueue,
    ICurrentUser currentUser,
    ILogger<ChatTurnService> logger)
{
    /// <summary>
    /// Makes the change to the scene a hint asks for, so the reply it steers is written into that scene. A hint that
    /// changes nothing succeeds.
    /// </summary>
    public Task<Result> DirectAsync(Guid sessionId, DirectorHint hint) => hint switch
    {
        DirectorHint.BringIn(var characterId) => sessions.BringInAsync(sessionId, characterId),
        DirectorHint.MoveTo(var location) => sessions.MoveSceneAsync(sessionId, location),
        _ => Task.FromResult(Result.Success()),
    };

    /// <summary>Streams the next reply, steered by <paramref name="hint"/> for this reply only; saving is the caller's step, so a stopped reply keeps what it got.</summary>
    public Task<Result> StreamReplyAsync(Guid sessionId, Action<string> onText, DirectorHint? hint = null, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "stream a reply", cancellationToken, async () =>
        {
            var loaded = await sessions.GetAsync(sessionId, cancellationToken);
            if (loaded.IsFailure)
            {
                return loaded;
            }

            var session = loaded.Value;
            if (session.ChatModelProfileId is not { } profileId)
            {
                logger.LogWarning("Chat {SessionId} has no chat model", sessionId);
                return Result.Failure("Pick a chat model for this chat");
            }

            var profileResult = await profiles.GetAsync(profileId, cancellationToken);
            if (profileResult.IsFailure)
            {
                return profileResult;
            }

            var profile = profileResult.Value;
            var created = clients.Create(profile);
            if (created.IsFailure)
            {
                logger.LogWarning("Model profile {ProfileId} cannot be turned into a client", profile.Id);
                return created;
            }

            var recalled = await recall.RecallAsync(sessionId, session.Messages, cancellationToken);
            if (recalled.IsCancelled)
            {
                return recalled;
            }

            // Memories enrich a reply; failing to recall them must not stop one.
            var memories = recalled.IsSuccess ? recalled.Value : [];
            using var client = created.Value;
            var messages = PromptBuilder.Build(InputFor(session, memories) with { Hint = hint }, PromptBudget.For(profile));

            return await ProviderErrors.TranslateAsync(profile, logger, async () =>
            {
                var characters = 0;
                await foreach (var update in client.GetStreamingResponseAsync(messages, ChatClientFactory.OptionsFor(profile), cancellationToken))
                {
                    if (update.Text is { Length: > 0 } text)
                    {
                        characters += text.Length;
                        onText(text);
                    }
                }

                logger.LogInformation("Streamed a {CharacterCount}-character reply in chat {SessionId}", characters, sessionId);
                return Result.Success(true);
            });
        });

    /// <summary>
    /// Saves a streamed reply as one message per speaker, then queues scene and memory upkeep for the chat without
    /// waiting on it.
    /// </summary>
    public Task<Result<IReadOnlyList<Message>>> SaveReplyAsync(Guid sessionId, string text) =>
        ThenUpkeepAsync(sessions.AddReplyAsync(sessionId, present => SpeakerTagParser.Parse(text, present)), _ => sessionId);

    /// <summary>Rewords one of the user's messages and drops everything after it; the reply to it is streamed next.</summary>
    public Task<Result> EditAsync(Guid sessionId, Guid messageId, string text) =>
        ThenUpkeepAsync(history.EditUserMessageAsync(sessionId, messageId, text), _ => sessionId);

    /// <summary>Drops the last reply and what it left in the scene and memory; the new one is streamed next.</summary>
    public Task<Result> TakeBackReplyAsync(Guid sessionId) =>
        ThenUpkeepAsync(history.TakeBackReplyAsync(sessionId), _ => sessionId);

    public Task<Result<ChatSession>> BranchAsync(Guid sessionId, Guid messageId) =>
        ThenUpkeepAsync(history.BranchAsync(sessionId, messageId), branched => branched.Value.Id);

    private async Task<TResult> ThenUpkeepAsync<TResult>(Task<TResult> change, Func<TResult, Guid> chatOf)
        where TResult : Result
    {
        var result = await change;
        if (result.IsSuccess)
        {
            await QueueUpkeepAsync(chatOf(result));
        }

        return result;
    }

    private async Task QueueUpkeepAsync(Guid sessionId)
    {
        if (await currentUser.GetUserIdAsync() is { Length: > 0 } ownerId)
        {
            upkeepQueue.Enqueue(new UpkeepJob(ownerId, sessionId));
        }
    }

    private static PromptInput InputFor(ChatSession session, IReadOnlyList<MemoryEntry> memories) => new(
        session.Scenario.Chatbot,
        session.Scenario,
        session.Persona,
        session.Scene,
        session.PresentStates(session.CharacterStates).Select(s => new PresentCharacter(s.Character, s)).ToList(),
        session.AbsentStates(session.CharacterStates).Select(s => new PresentCharacter(s.Character, s)).ToList(),
        session.Summary,
        memories,
        session.Messages);
}
