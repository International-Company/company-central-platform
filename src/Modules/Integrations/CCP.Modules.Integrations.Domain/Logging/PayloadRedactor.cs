using System.Text;
using System.Text.Json;

namespace CCP.Modules.Integrations.Domain.Logging;

/// <summary>
/// Blanks the fields a provider declared sensitive, before anything is written
/// down.
/// <para>
/// <b>The call log is the most valuable thing this module produces and the most
/// dangerous.</b> It is what makes an integration dispute resolvable — "we sent
/// them this, at this time, and they answered that" — and it is also a permanent
/// record of every payload the company exchanged. A card number written there is
/// written there for the length of the retention policy, in a table many people
/// can read.
/// </para>
/// <para>
/// So redaction happens <b>before storage</b>, not on the way out. Storing the
/// real payload and hiding it at read time leaves the secret in the database,
/// where the next query, the next export and the next backup will find it.
/// </para>
/// </summary>
public static class PayloadRedactor
{
    /// <summary>What replaces a redacted value. Recognisable, and not a value.</summary>
    public const string Mask = "[redacted]";

    /// <summary>
    /// How much of a payload is kept at all.
    /// <para>
    /// A log row is evidence, not an archive. Something has to bound it, or one
    /// provider returning a large document turns this table into the largest in
    /// the Platform.
    /// </para>
    /// </summary>
    public const int MaxLength = 8 * 1024;

    /// <summary>
    /// Redacts the named fields, at any depth.
    /// <para>
    /// Matching is on the field name, case-insensitively, wherever it appears.
    /// Path-based matching would be more precise and would miss the same field
    /// nested one level deeper than whoever wrote the policy expected — and the
    /// failure mode of being too precise here is a leak.
    /// </para>
    /// <para>
    /// A payload that is not JSON is <b>not</b> passed through. It cannot be
    /// inspected, so it cannot be shown to be safe, and the honest thing is to
    /// record its shape rather than its content.
    /// </para>
    /// </summary>
    public static string Redact(string? payload, IReadOnlyCollection<string> fieldNames)
    {
        ArgumentNullException.ThrowIfNull(fieldNames);

        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        if (fieldNames.Count == 0)
        {
            return Truncate(payload);
        }

        var sensitive = new HashSet<string>(fieldNames, StringComparer.OrdinalIgnoreCase);

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);

            var buffer = new MemoryStream();

            using (var writer = new Utf8JsonWriter(buffer))
            {
                Write(document.RootElement, writer, sensitive, propertyName: null);
            }

            return Truncate(Encoding.UTF8.GetString(buffer.ToArray()));
        }
        catch (JsonException)
        {
            // Not JSON. Form-encoded, XML, a binary blob — none of which this
            // can walk. Recording its length rather than its content is worth
            // less to an investigation and cannot leak anything.
            return $"[unparsed payload, {payload.Length} characters]";
        }
    }

    private static void Write(
        JsonElement element,
        Utf8JsonWriter writer,
        HashSet<string> sensitive,
        string? propertyName)
    {
        // The check is on the name of the property this value arrived under, so
        // an object or an array named "credentials" is masked whole rather than
        // walked into.
        if (propertyName is not null && sensitive.Contains(propertyName))
        {
            writer.WriteStringValue(Mask);

            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    Write(property.Value, writer, sensitive, property.Name);
                }

                writer.WriteEndObject();

                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();

                foreach (JsonElement item in element.EnumerateArray())
                {
                    // The array's own name has already been checked; its items
                    // carry no name of their own.
                    Write(item, writer, sensitive, propertyName: null);
                }

                writer.WriteEndArray();

                break;

            default:
                element.WriteTo(writer);

                break;
        }
    }

    private static string Truncate(string value) =>
        value.Length <= MaxLength
            ? value
            : string.Concat(value.AsSpan(0, MaxLength), "… [truncated]");
}
