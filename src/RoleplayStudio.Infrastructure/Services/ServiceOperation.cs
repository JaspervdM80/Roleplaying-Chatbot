using Microsoft.Extensions.Logging;

namespace RoleplayStudio.Infrastructure.Services;

public static class ServiceOperation
{
    public static Task<Result> RunAsync(ILogger logger, string operation, CancellationToken cancellationToken, Func<Task<Result>> body) =>
        RunCoreAsync(logger, operation, cancellationToken, body, Result.Cancelled, () => Result.Failure("Could not {0}", operation));

    public static Task<Result<T>> RunAsync<T>(ILogger logger, string operation, CancellationToken cancellationToken, Func<Task<Result<T>>> body) =>
        RunCoreAsync(logger, operation, cancellationToken, body, () => Result.Cancelled().To<T>(), () => Result.Failure<T>("Could not {0}", operation));

    public static Task<Result> RunOwnerAsync(ICurrentUser currentUser, ILogger logger, string operation, CancellationToken cancellationToken, Func<string, Task<Result>> body) =>
        RunAsync(logger, operation, cancellationToken, async () =>
            await currentUser.GetUserIdAsync() is { Length: > 0 } ownerId ? await body(ownerId) : NotSignedIn(logger, operation));

    public static Task<Result<T>> RunOwnerAsync<T>(ICurrentUser currentUser, ILogger logger, string operation, CancellationToken cancellationToken, Func<string, Task<Result<T>>> body) =>
        RunAsync(logger, operation, cancellationToken, async () =>
            await currentUser.GetUserIdAsync() is { Length: > 0 } ownerId ? await body(ownerId) : NotSignedIn(logger, operation).To<T>());

    private static Result NotSignedIn(ILogger logger, string operation)
    {
        logger.LogWarning("Refused to {Operation}: nobody is signed in", operation);
        return Result.Failure("Sign in to {0}", operation);
    }

    private static async Task<TResult> RunCoreAsync<TResult>(
        ILogger logger,
        string operation,
        CancellationToken cancellationToken,
        Func<Task<TResult>> body,
        Func<TResult> cancelled,
        Func<TResult> failed)
    {
        try
        {
            return await body();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Cancelled {Operation}", operation);
            return cancelled();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to {Operation}", operation);
            return failed();
        }
    }
}
