using System.Text.RegularExpressions;
using CCP.Kernel.Domain;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;

namespace CCP.Modules.Notifications.Domain.Templates;

/// <summary>
/// What a message says, in one language.
/// <para>
/// <b>Every template exists in Arabic and English</b> (ARCHITECTURE.md §17.3).
/// One row per locale, and a send in a locale with no template is refused rather
/// than falling back — a director receiving an approval request in the wrong
/// language is a failure the company sees, and silently substituting English is
/// how that ships.
/// </para>
/// <para>
/// Variables are <b>declared</b>, not inferred from the body. A template that
/// discovers its variables by scanning itself accepts a typo as a new variable
/// and renders an empty space where a number should be; declaring them means a
/// missing one is an error at the boundary instead.
/// </para>
/// </summary>
public sealed class NotificationTemplate : AggregateRoot, IAuditableEntity
{
    /// <summary>
    /// A placeholder: <c>{{name}}</c>. Deliberately not an expression syntax.
    /// <para>
    /// Substitution only — no conditionals, no loops, no property paths. A
    /// template language is a language, and a language in a database is code
    /// nobody reviews running with the Platform's privileges.
    /// </para>
    /// </summary>
    private static readonly Regex Placeholder = new(
        @"\{\{\s*([a-zA-Z][a-zA-Z0-9_]*)\s*\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private NotificationTemplate() { }

    private NotificationTemplate(
        Guid id,
        string code,
        string locale,
        string subject,
        string body,
        IReadOnlyList<string> variables,
        DateTimeOffset now)
        : base(id)
    {
        Code = code;
        Locale = locale;
        Subject = subject;
        Body = body;
        Variables = string.Join(',', variables);
        Version = 1;
        IsActive = true;
        CreatedAt = now;
    }

    /// <summary>What this message is, across every locale and version.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary><c>ar</c> or <c>en</c>.</summary>
    public string Locale { get; private set; } = string.Empty;

    /// <summary>Used as an email subject; ignored by channels that have none.</summary>
    public string Subject { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    /// <summary>
    /// The variables this template requires, comma-separated.
    /// <para>
    /// Stored flat because it is only ever read whole. A join table for a list
    /// of short names read once per send is a table nobody thanks you for.
    /// </para>
    /// </summary>
    public string Variables { get; private set; } = string.Empty;

    public int Version { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    /// <summary>The declared variables, as a list.</summary>
    public IReadOnlyList<string> DeclaredVariables =>
        Variables.Length == 0
            ? []
            : [.. Variables.Split(',', StringSplitOptions.RemoveEmptyEntries
                                     | StringSplitOptions.TrimEntries)];

    public static Result<NotificationTemplate> Create(
        string code,
        string locale,
        string subject,
        string body,
        IReadOnlyList<string> variables,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(variables);

        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure<NotificationTemplate>(NotificationErrors.TemplateCodeRequired);
        }

        if (locale is not ("ar" or "en"))
        {
            return Result.Failure<NotificationTemplate>(NotificationErrors.UnsupportedLocale(locale));
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return Result.Failure<NotificationTemplate>(NotificationErrors.TemplateBodyRequired);
        }

        // Every placeholder in the text must be declared. The reverse — a
        // declared variable the body never uses — is allowed: a caller supplying
        // something unused is harmless, while a body referring to something
        // nobody supplies renders a gap in a message that has already been sent.
        var declared = variables.ToHashSet(StringComparer.Ordinal);

        var undeclared = Placeholder
            .Matches($"{subject} {body}")
            .Select(match => match.Groups[1].Value)
            .Where(name => !declared.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (undeclared.Count > 0)
        {
            return Result.Failure<NotificationTemplate>(
                NotificationErrors.UndeclaredVariables(undeclared));
        }

        return Result.Success(new NotificationTemplate(
            Uuid7.NewGuid(now), code.Trim().ToLowerInvariant(), locale,
            subject.Trim(), body.Trim(), [.. declared.Order(StringComparer.Ordinal)], now));
    }

    /// <summary>
    /// Renders the template with the supplied values.
    /// <para>
    /// <b>Every value is escaped before it is substituted</b>, without exception
    /// and with no way to opt out. A notification body reaches an email client
    /// and an in-app inbox, both of which render markup, and a display name
    /// containing a script tag is the whole of a stored cross-site scripting
    /// attack if it arrives unescaped.
    /// </para>
    /// <para>
    /// A missing variable is an error, not an empty space. The alternative is a
    /// message that says "Your request for  has been approved" — sent, read, and
    /// impossible to unsend.
    /// </para>
    /// </summary>
    public Result<RenderedMessage> Render(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var missing = DeclaredVariables
            .Where(name => !values.ContainsKey(name))
            .ToList();

        if (missing.Count > 0)
        {
            return Result.Failure<RenderedMessage>(NotificationErrors.MissingVariables(missing));
        }

        return Result.Success(new RenderedMessage(
            Substitute(Subject, values),
            Substitute(Body, values)));
    }

    /// <summary>Replaces this template's text with a new version.</summary>
    public Result Revise(
        string subject,
        string body,
        IReadOnlyList<string> variables,
        DateTimeOffset now)
    {
        Result<NotificationTemplate> validated = Create(Code, Locale, subject, body, variables, now);

        if (validated.IsFailure)
        {
            return Result.Failure(validated.Errors);
        }

        Subject = validated.Value.Subject;
        Body = validated.Value.Body;
        Variables = validated.Value.Variables;

        // Versioned so a delivery log entry can say which text was actually
        // sent. A template edited after the fact would otherwise rewrite the
        // history of every message it ever produced.
        Version++;
        UpdatedAt = now;

        return Result.Success();
    }

    public Result SetActive(bool isActive, DateTimeOffset now)
    {
        if (IsActive == isActive)
        {
            return Result.Failure(NotificationErrors.TemplateAlreadyInThatState);
        }

        IsActive = isActive;
        UpdatedAt = now;

        return Result.Success();
    }

    private static string Substitute(string text, IReadOnlyDictionary<string, string> values)
        => Placeholder.Replace(
            text,
            match => Escape(values.GetValueOrDefault(match.Groups[1].Value, string.Empty)));

    /// <summary>
    /// Escapes the five characters that change how markup is read.
    /// <para>
    /// Applied to values, never to the template body: the body is written by an
    /// administrator and may legitimately contain markup, while a value comes
    /// from data and must not.
    /// </para>
    /// </summary>
    private static string Escape(string value)
        => value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal);
}

/// <summary>A template with its values filled in.</summary>
public sealed record RenderedMessage(string Subject, string Body);
