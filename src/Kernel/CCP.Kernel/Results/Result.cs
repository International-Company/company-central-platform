using System.Diagnostics.CodeAnalysis;

namespace CCP.Kernel.Results;

/// <summary>
/// The outcome of an operation that can fail for an expected reason.
/// <para>
/// Expected failures — validation, not found, conflict — are returned, not
/// thrown. Exceptions are reserved for genuinely unexpected conditions, which
/// keeps control flow visible and makes handlers easy to test.
/// </para>
/// </summary>
public class Result
{
    protected Result(bool isSuccess, IReadOnlyList<Error> errors)
    {
        if (isSuccess && errors.Count > 0)
        {
            throw new InvalidOperationException("A successful result cannot carry errors.");
        }

        if (!isSuccess && errors.Count == 0)
        {
            throw new InvalidOperationException("A failed result must carry at least one error.");
        }

        IsSuccess = isSuccess;
        Errors = errors;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public IReadOnlyList<Error> Errors { get; }

    /// <summary>The first error. Throws when the result is a success.</summary>
    public Error Error => IsFailure
        ? Errors[0]
        : throw new InvalidOperationException("A successful result has no error.");

    public static Result Success() => new(true, []);

    public static Result Failure(Error error) => new(false, [error]);

    public static Result Failure(IReadOnlyList<Error> errors) => new(false, errors);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, []);

    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, [error]);

    public static Result<TValue> Failure<TValue>(IReadOnlyList<Error> errors) => new(default, false, errors);

    /// <summary>
    /// Combines this result with another, accumulating errors from both.
    /// <para>
    /// Used to validate several fields and report every problem at once.
    /// Returning only the first error makes a user fix one field, resubmit,
    /// and discover the next — which is a worse experience than it needs to be,
    /// and the API error contract already carries a list (ADR-008).
    /// </para>
    /// </summary>
    public Result Combine(Result other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (IsSuccess && other.IsSuccess)
        {
            return Success();
        }

        return Failure([.. Errors, .. other.Errors]);
    }

    /// <summary>Combines any number of results, accumulating every error.</summary>
    public static Result Combine(params ReadOnlySpan<Result> results)
    {
        List<Error> errors = [];

        foreach (Result result in results)
        {
            if (result.IsFailure)
            {
                errors.AddRange(result.Errors);
            }
        }

        return errors.Count == 0 ? Success() : Failure(errors);
    }
}

/// <summary>The outcome of an operation that returns a value when it succeeds.</summary>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, IReadOnlyList<Error> errors)
        : base(isSuccess, errors)
    {
        _value = value;
    }

    /// <summary>The value produced. Throws when the result is a failure.</summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result has no value.");

    /// <summary>Attempts to read the value without throwing.</summary>
    public bool TryGetValue([NotNullWhen(true)] out TValue? value)
    {
        value = IsSuccess ? _value : default;
        return IsSuccess && value is not null;
    }

    public static implicit operator Result<TValue>(TValue value) => Success(value);

    public static implicit operator Result<TValue>(Error error) => Failure<TValue>(error);
}
