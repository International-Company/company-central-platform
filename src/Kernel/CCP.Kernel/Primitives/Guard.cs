using System.Runtime.CompilerServices;

namespace CCP.Kernel.Primitives;

/// <summary>
/// Argument checks for invariants that must never be violated.
/// <para>
/// These guard against programmer error, not user input. User input is
/// validated at the API boundary and returns a <see cref="Results.Result"/>;
/// reaching a guard failure means a bug, so it throws.
/// </para>
/// </summary>
public static class Guard
{
    public static T NotNull<T>(T? value, [CallerArgumentExpression(nameof(value))] string? name = null)
        where T : class
        => value ?? throw new ArgumentNullException(name);

    public static string NotNullOrWhiteSpace(
        string? value,
        [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value must not be null or whitespace.", name);
        }

        return value;
    }

    public static string MaxLength(
        string value,
        int maxLength,
        [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value.Length > maxLength)
        {
            throw new ArgumentException($"Value must be at most {maxLength} characters.", name);
        }

        return value;
    }

    public static Guid NotEmpty(Guid value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Value must not be an empty GUID.", name);
        }

        return value;
    }

    public static int InRange(
        int value,
        int min,
        int max,
        [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(name, value, $"Value must be between {min} and {max}.");
        }

        return value;
    }
}
