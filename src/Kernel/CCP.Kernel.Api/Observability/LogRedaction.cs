using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace CCP.Kernel.Api.Observability;

/// <summary>
/// Removes credentials from log events, at the sink.
/// <para>
/// <b>The architecture says "applied at the sink, not left to the discipline of
/// whoever writes the log statement" (§22.2), and that phrasing is the whole
/// design.</b> Every leak of this kind is written by somebody being careful who
/// did not know that the object they logged had a token three properties down,
/// or that the exception message contained the request body. Discipline does not
/// scale to every log statement anybody will ever write.
/// </para>
/// <para>
/// So this runs over every event on its way out: property names that mean a
/// credential are replaced whole, and values that look like one are replaced
/// wherever they appear in a message.
/// </para>
/// <para>
/// <b>What it is not.</b> It is a last line, not the only one — a redactor that
/// people rely on instead of not logging secrets will eventually meet a shape it
/// does not recognise. It costs a scan of each event and removes the most common
/// accidents.
/// </para>
/// </summary>
public sealed partial class LogRedactionEnricher : ILogEventEnricher
{
    /// <summary>What a redacted value reads as.</summary>
    public const string Mask = "[redacted]";

    /// <summary>
    /// Property names that always carry something that must not be written down.
    /// <para>
    /// Matched as a substring, case-insensitively, so <c>refreshToken</c>,
    /// <c>RefreshTokenHash</c> and <c>token</c> are all caught by one entry.
    /// Broad on purpose: a false positive costs a log line some detail, and a
    /// false negative costs a credential.
    /// </para>
    /// </summary>
    private static readonly string[] SensitiveNames =
    [
        "password",
        "secret",
        "token",
        "credential",
        "authorization",
        "apikey",
        "api_key",
        "recoverycode",
        "privatekey",
        "connectionstring"
    ];

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        foreach (KeyValuePair<string, LogEventPropertyValue> property in logEvent.Properties)
        {
            if (IsSensitiveName(property.Key))
            {
                logEvent.AddOrUpdateProperty(
                    new LogEventProperty(property.Key, new ScalarValue(Mask)));

                continue;
            }

            if (property.Value is ScalarValue { Value: string text } && LooksLikeCredential(text))
            {
                logEvent.AddOrUpdateProperty(
                    new LogEventProperty(property.Key, new ScalarValue(Mask)));
            }
        }
    }

    private static bool IsSensitiveName(string name)
    {
        foreach (string sensitive in SensitiveNames)
        {
            if (name.Contains(sensitive, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a value looks like a credential whatever it was called.
    /// <para>
    /// Catches the case the name check cannot: an exception message, a URL with
    /// a token in it, an object whose property is called something innocuous.
    /// </para>
    /// </summary>
    public static bool LooksLikeCredential(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 20)
        {
            return false;
        }

        return BearerOrJwt().IsMatch(value) || KnownPrefix().IsMatch(value);
    }

    [GeneratedRegex(
        @"(Bearer\s+[\w\-\.]+|eyJ[\w\-]+\.[\w\-]+\.[\w\-]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex BearerOrJwt();

    [GeneratedRegex(
        @"(-----BEGIN|ccps_[\w\-]{10,}|sk-[\w\-]{10,}|AKIA[0-9A-Z]{12,}|gh[pous]_[\w]{20,})",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex KnownPrefix();
}
