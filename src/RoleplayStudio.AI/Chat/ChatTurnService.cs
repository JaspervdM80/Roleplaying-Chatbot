using Microsoft.Extensions.Logging;
using RoleplayStudio.AI.Providers;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.AI.Chat;

public sealed class ChatTurnService(ChatSessionService sessions, ModelProfileService profiles, IChatClientFactory clients, ILogger<ChatTurnService> logger)
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

            using var client = created.Value;
            var messages = SessionPrompt.Build(InputFor(session));

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

    private static SessionPromptInput InputFor(ChatSession session) => new(
        session.Scenario.Chatbot,
        session.Scenario,
        session.Persona,
        session.Scene,
        session.PresentStates(session.CharacterStates).Select(s => new PresentCharacter(s.Character, s)).ToList(),
        session.Messages);
}
