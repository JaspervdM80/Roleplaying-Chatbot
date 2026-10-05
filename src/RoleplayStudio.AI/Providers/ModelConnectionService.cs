using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.AI.Providers;

public sealed record ConnectionCheck(TimeSpan Latency, string Reply);

public sealed class ModelConnectionService(ModelProfileService profiles, IChatClientFactory clients, ILogger<ModelConnectionService> logger)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public Task<Result<ConnectionCheck>> TestAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "test the model connection", cancellationToken, async () =>
        {
            var loaded = await profiles.GetAsync(profileId, cancellationToken);
            if (loaded.IsFailure)
            {
                return loaded.To<ConnectionCheck>();
            }

            var profile = loaded.Value;
            var created = clients.Create(profile);
            if (created.IsFailure)
            {
                logger.LogWarning("Model profile {ProfileId} cannot be turned into a client", profile.Id);
                return created.To<ConnectionCheck>();
            }

            using var client = created.Value;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);

            return await ProviderErrors.TranslateAsync(profile, logger, async () =>
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var response = await client.GetResponseAsync(
                        [new ChatMessage(ChatRole.User, "Reply with the single word OK.")],
                        new ChatOptions { MaxOutputTokens = 16 },
                        timeout.Token);
                    logger.LogInformation("Model profile {ProfileId} answered a connection test in {LatencyMs} ms", profile.Id, stopwatch.ElapsedMilliseconds);
                    return Result.Success(new ConnectionCheck(stopwatch.Elapsed, response.Text.Trim()));
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning("Model profile {ProfileId} did not answer a connection test in time", profile.Id);
                    return Result.Failure<ConnectionCheck>("No answer within {0} seconds", Timeout.TotalSeconds);
                }
            });
        });
}
