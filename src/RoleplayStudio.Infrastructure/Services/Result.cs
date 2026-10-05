namespace RoleplayStudio.Infrastructure.Services;

public class Result
{
    protected Result(bool isSuccess, bool isCancelled, string? errorTemplate, object?[] errorArguments)
    {
        IsSuccess = isSuccess;
        IsCancelled = isCancelled;
        ErrorTemplate = errorTemplate;
        ErrorArguments = errorArguments;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public bool IsCancelled { get; }

    /// <summary>The untranslated template, kept apart from its arguments so it can be localized and grouped in logs.</summary>
    public string? ErrorTemplate { get; }
    public object?[] ErrorArguments { get; }

    public string? Error => ErrorTemplate is null ? null : string.Format(ErrorTemplate, ErrorArguments);

    public static Result Success() => new(true, false, null, []);
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
    public static Result Failure(string template, params object?[] arguments) => new(false, false, template, arguments);
    public static Result<T> Failure<T>(string template, params object?[] arguments) => Failure(template, arguments).To<T>();
    public static Result Cancelled() => new(false, true, null, []);

    public Result<T> To<T>() => IsSuccess
        ? throw new InvalidOperationException("A successful result has no value to convert.")
        : Result<T>.FromFailure(this);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T? value, bool isSuccess, bool isCancelled, string? errorTemplate, object?[] errorArguments)
        : base(isSuccess, isCancelled, errorTemplate, errorArguments)
    {
        _value = value;
    }

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("A failed result has no value.");

    internal static Result<T> Success(T value) => new(value, true, false, null, []);
    internal static Result<T> FromFailure(Result failure) => new(default, false, failure.IsCancelled, failure.ErrorTemplate, failure.ErrorArguments);

    public static implicit operator Result<T>(T value) => Success(value);
}
