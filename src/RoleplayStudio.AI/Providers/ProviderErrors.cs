using System.ClientModel;
using System.Net;
using Microsoft.Extensions.Logging;
using OllamaSharp.Models.Exceptions;
using RoleplayStudio.AI.Images;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.AI.Providers;

/// <summary>Turns what a provider throws for a wrong key, model or address into a readable failure; anything else still propagates.</summary>
public static class ProviderErrors
{
    public static async Task<Result<T>> TranslateAsync<T>(ModelProfile profile, ILogger logger, Func<Task<Result<T>>> call)
    {
        try
        {
            return await call();
        }
        catch (ClientResultException exception) when (exception.Status == 0)
        {
            return Log<T>(profile, logger, exception, Result.Failure("Could not reach {0}", profile.Address));
        }
        catch (ClientResultException exception)
        {
            return Failure<T>(profile, logger, exception.Status, exception);
        }
        catch (HttpRequestException exception)
        {
            return exception.StatusCode is { } status
                ? Failure<T>(profile, logger, (int)status, exception)
                : Log<T>(profile, logger, exception, Result.Failure("Could not reach {0}", profile.Address));
        }
        catch (ImageProviderException exception)
        {
            return Log<T>(profile, logger, exception, Result.Failure("The image provider refused the request: {0}", exception.Parameter is null ? exception.Code : $"{exception.Code} ({exception.Parameter})"));
        }
        catch (OllamaException exception)
        {
            return Log<T>(profile, logger, exception, Result.Failure("Ollama refused the request: {0}", exception.Message));
        }
    }

    private static Result<T> Failure<T>(ModelProfile profile, ILogger logger, int status, Exception exception) => Log<T>(profile, logger, exception, status switch
    {
        (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden => Result.Failure("The provider rejected the API key in {0}", profile.ApiKeySetting ?? "(none)"),
        (int)HttpStatusCode.NotFound => Result.Failure("The provider does not know the model {0}", profile.ModelId),
        (int)HttpStatusCode.PaymentRequired => Result.Failure("The provider account is out of credit"),
        (int)HttpStatusCode.TooManyRequests => Result.Failure("The provider is rate limiting; try again shortly"),
        _ => Result.Failure("The provider answered with status {0}", status),
    });

    private static Result<T> Log<T>(ModelProfile profile, ILogger logger, Exception exception, Result failure)
    {
        logger.LogWarning("Model profile {ProfileId} failed with {ExceptionType}", profile.Id, exception.GetType().Name);
        logger.LogDebug(exception, "Provider failure for model profile {ProfileId}", profile.Id);
        return failure.To<T>();
    }
}
