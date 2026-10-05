using Microsoft.Extensions.Logging;
using RoleplayStudio.AI.Providers;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.AI.Playground;

public sealed class PlaygroundChatService(ModelProfileService profiles, IChatClientFactory clients, ILogger<PlaygroundChatService> logger)
{
    /// <summary>Streams the character's reply into <paramref name="onText"/>; on cancellation the text already delivered is all there is.</summary>
    public Task<Result> StreamReplyAsync(
        Guid profileId,
        PlaygroundCharacter character,
        IReadOnlyList<PlaygroundTurn> history,
        Action<string> onText,
        CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "stream a reply", cancellationToken, async () =>
        {
            if (string.IsNullOrWhiteSpace(character.Name))
            {
                return Result.Failure("Give the character a name");
            }

            var loaded = await profiles.GetAsync(profileId, cancellationToken);
            if (loaded.IsFailure)
            {
                return loaded;
            }

            var profile = loaded.Value;
            var created = clients.Create(profile);
            if (created.IsFailure)
            {
                logger.LogWarning("Model profile {ProfileId} cannot be turned into a client", profile.Id);
                return created;
            }

            using var client = created.Value;
            var messages = PlaygroundPrompt.Build(character, history);

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

                logger.LogInformation("Streamed a {CharacterCount}-character playground reply from model profile {ProfileId}", characters, profile.Id);
                return Result.Success(true);
            });
        });
}
