using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using RoleplayStudio.AI.Images;
using RoleplayStudio.Domain.Media;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.AI.Providers;

/// <summary>An image model's test sets <see cref="ImageId"/>; <see cref="Reply"/> is then the prompt it drew.</summary>
public sealed record ConnectionCheck(TimeSpan Latency, string Reply, Guid? ImageId = null);

public sealed class ModelConnectionService(ModelProfileService profiles, IChatClientFactory clients, ImageService images, ILogger<ModelConnectionService> logger)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ImageTimeout = TimeSpan.FromSeconds(90);
    private const string TestPicture = "A lighthouse on a rocky coast at sunset, soft light, detailed illustration";
    private const int TestPictureSize = 1024;

    public Task<Result<ConnectionCheck>> TestAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "test the model connection", cancellationToken, async () =>
        {
            var loaded = await profiles.GetAsync(profileId, cancellationToken);
            if (loaded.IsFailure)
            {
                return loaded.To<ConnectionCheck>();
            }

            var profile = loaded.Value;
            if (profile.Role == ModelRole.Image)
            {
                return await DrawTestPictureAsync(profile, cancellationToken);
            }

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
                        // Thinking models count their reasoning against this cap; a tiny one leaves them no tokens to answer with.
                        new ChatOptions { MaxOutputTokens = 1024 },
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

    private async Task<Result<ConnectionCheck>> DrawTestPictureAsync(ModelProfile profile, CancellationToken cancellationToken)
    {
        var created = clients.CreateImageGenerator(profile);
        if (created.IsFailure)
        {
            logger.LogWarning("Model profile {ProfileId} cannot be turned into an image generator", profile.Id);
            return created.To<ConnectionCheck>();
        }

        var generator = created.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ImageTimeout);

        var drawn = await ProviderErrors.TranslateAsync(profile, logger, async () =>
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var request = new ImageRequest(TestPicture, null, TestPictureSize, TestPictureSize, Random.Shared.Next(1, int.MaxValue));
                var picture = await generator.GenerateAsync(request, timeout.Token);
                logger.LogInformation("Model profile {ProfileId} drew a test picture in {LatencyMs} ms", profile.Id, stopwatch.ElapsedMilliseconds);
                return Result.Success((picture, stopwatch.Elapsed));
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Model profile {ProfileId} did not draw a test picture in time", profile.Id);
                return Result.Failure<(GeneratedPicture, TimeSpan)>("No picture within {0} seconds", ImageTimeout.TotalSeconds);
            }
        });
        if (drawn.IsFailure)
        {
            return drawn.To<ConnectionCheck>();
        }

        var (picture, latency) = drawn.Value;
        var saved = await images.SaveAsync(
            new GeneratedImage
            {
                Prompt = TestPicture,
                Provider = profile.Provider.ToString(),
                Model = generator.ModelId,
                Seed = picture.Seed,
                ContentType = picture.ContentType,
                Width = TestPictureSize,
                Height = TestPictureSize,
            },
            picture.Data);
        return saved.IsSuccess ? Result.Success(new ConnectionCheck(latency, TestPicture, saved.Value.Id)) : saved.To<ConnectionCheck>();
    }
}
