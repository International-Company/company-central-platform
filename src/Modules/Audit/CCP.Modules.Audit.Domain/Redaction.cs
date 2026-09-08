using System.Text.Json;
using System.Text.Json.Nodes;

namespace CCP.Modules.Audit.Domain;

/// <summary>
/// Removes secrets from audit payloads <b>before</b> they are stored
/// (ARCHITECTURE.md §15.7).
/// <para>
/// Before, not after. A redaction pass that runs on the way out leaves the
/// secret sitting in an append-only table that, by design, nobody can go back
/// and clean. The one place this can be done is the one place it is done.
/// </para>
/// <para>
/// <b>Deny by field name, not by value.</b> Trying to recognise a secret by
/// looking at it is guesswork — a password can be any string. A field called
/// <c>password</c> holds one whatever it contains, and that is knowable.
/// </para>
/// <para>
/// The list is matched as a <i>substring</i>, case-insensitively, so
/// <c>newPassword</c>, <c>password_hash</c> and <c>PasswordConfirmation</c> are
/// all caught without anyone having to enumerate them. Over-redacting an
/// innocent field is a small loss; under-redacting a credential is a breach.
/// </para>
/// </summary>
public static class Redaction
{
    /// <summary>What replaces a redacted value. Deliberately obvious in a trail.</summary>
    public const string Placeholder = "[REDACTED]";

    /// <summary>
    /// Field-name fragments that mark a value as never storable.
    /// <para>
    /// Everything a credential can be called, plus the things that are
    /// credential-equivalent: a token, a recovery code, a TOTP secret, a private
    /// key. <c>otp</c> and <c>mfa</c> are here because an authenticator secret
    /// is as good as a password.
    /// </para>
    /// </summary>
    private static readonly string[] SensitiveFragments =
    [
        "password",
        "passphrase",
        "secret",
        "token",
        "credential",
        "apikey",
        "api_key",
        "privatekey",
        "private_key",
        "signingkey",
        "signing_key",
        "recoverycode",
        "recovery_code",
        "authorization",
        "cookie",
        "otp",
        "totp",
        "mfa",
        "pin",
        "cvv",
        "salt",
        "hash"
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Redacts a JSON document, at any depth.
    /// <para>
    /// Non-JSON input is returned unchanged: this is a filter for structured
    /// payloads, and silently mangling a plain string would be worse than
    /// leaving it. Callers do not put credentials in free text, and the
    /// <c>old_value</c>/<c>new_value</c> columns are <c>jsonb</c>.
    /// </para>
    /// </summary>
    public static string? Apply(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        JsonNode? node;

        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return json;
        }

        if (node is null)
        {
            return json;
        }

        Redact(node);

        return node.ToJsonString(SerializerOptions);
    }

    /// <summary>Whether a field name means "never store this value".</summary>
    public static bool IsSensitive(string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);

        return SensitiveFragments.Any(
            fragment => propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Walks the document, replacing sensitive values in place.
    /// <para>
    /// A sensitive name redacts the <b>whole subtree</b>, not just a scalar. An
    /// object called <c>credentials</c> holds nothing that should survive, and
    /// descending into it to redact field by field would preserve exactly the
    /// structure an attacker wants.
    /// </para>
    /// </summary>
    private static void Redact(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (string key in obj.Select(p => p.Key).ToList())
                {
                    if (IsSensitive(key))
                    {
                        obj[key] = Placeholder;
                    }
                    else if (obj[key] is { } child)
                    {
                        Redact(child);
                    }
                }

                break;

            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    if (item is not null)
                    {
                        Redact(item);
                    }
                }

                break;
        }
    }
}
