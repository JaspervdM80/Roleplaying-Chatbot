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
    ModelProfileService profiles,
    IChatClientFactory clients,
    MemoryRecall recall,
    UpkeepQueue upkeepQueue,
    ICurrentUser currentUser,
    ILogger<ChatTurnService> logger)
{
    /// <summary>
    /// Streams the next reply to the chat as it stands into <paramref name="onText"/>. Saving it is the caller's
    /// step, so a stopped reply is kept as far as it got.
    /// </summary>
    public Task<Result> StreamReplyAsync(Guid sessionId, Action<string> onText, CancellationToken cancellationToken = default) =>
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
            var messages = PromptBuilder.Build(InputFor(session, memories), PromptBudget.For(profile));

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

    /// <summary>Saves a streamed reply, then queues scene and memory upkeep for the chat without waiting on it.</summary>
    public async Task<Result<Message>> SaveReplyAsync(Guid sessionId, string text)
    {
        var saved = await sessions.AddReplyAsync(sessionId, text);
        if (saved.IsSuccess && await currentUser.GetUserIdAsync() is { Length: > 0 } ownerId)
        {
            upkeepQueue.Enqueue(new UpkeepJob(ownerId, sessionId));
        }

        return saved;
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
