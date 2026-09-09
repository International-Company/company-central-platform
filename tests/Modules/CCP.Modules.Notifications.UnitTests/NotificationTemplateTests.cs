using CCP.Kernel.Results;
using CCP.Modules.Notifications.Domain.Templates;
using CCP.Modules.Notifications.Infrastructure;

namespace CCP.Modules.Notifications.UnitTests;

/// <summary>
/// What a template refuses, and what it does to the values it is given.
/// <para>
/// The escaping tests are the ones that matter most. A notification body reaches
/// an email client and an in-app inbox, both of which render markup, so a display
/// name containing a script tag is the whole of a stored cross-site-scripting
/// attack if it arrives unescaped — and it arrives from user data, which is
/// exactly where an attacker puts it.
/// </para>
/// </summary>
public sealed class NotificationTemplateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void APlaceholderTheTemplateDoesNotDeclareIsRefused()
    {
        Result<NotificationTemplate> created = NotificationTemplate.Create(
            "test", "en", "Hello", "Hello {{name}} of {{department}}", ["name"], Now);

        // Caught at authoring time. The alternative is a message already sent
        // with a gap where a department should be.
        Assert.True(created.IsFailure);
        Assert.Equal("NOTIFICATIONS.UNDECLARED_VARIABLES", created.Errors[0].Code);
        Assert.Contains("department", created.Errors[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeclaredVariableTheBodyNeverUsesIsAllowed()
    {
        // The asymmetry is deliberate: a caller supplying something unused is
        // harmless, while a body referring to something nobody supplies renders
        // a gap in a message that has already gone out.
        Assert.True(NotificationTemplate.Create(
            "test", "en", "Hi", "Hello {{name}}", ["name", "unused"], Now).IsSuccess);
    }

    [Fact]
    public void RenderingWithoutAValueIsRefused()
    {
        NotificationTemplate template = Template("Hello {{name}}", ["name"]);

        Result<RenderedMessage> rendered = template.Render(
            new Dictionary<string, string>(StringComparer.Ordinal));

        // Not an empty space. "Your request for  has been approved" is sent,
        // read, and impossible to unsend.
        Assert.True(rendered.IsFailure);
        Assert.Equal("NOTIFICATIONS.MISSING_VARIABLES", rendered.Errors[0].Code);
    }

    [Fact]
    public void ValuesAreEscaped()
    {
        NotificationTemplate template = Template("Hello {{name}}", ["name"]);

        Result<RenderedMessage> rendered = template.Render(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "<script>alert('x')</script>"
            });

        Assert.True(rendered.IsSuccess);

        // The attack, neutralised. Every one of the five characters that change
        // how markup is read is replaced.
        Assert.DoesNotContain("<script>", rendered.Value.Body, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", rendered.Value.Body, StringComparison.Ordinal);
        Assert.Contains("&#39;", rendered.Value.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBodyItselfIsNotEscaped()
    {
        NotificationTemplate template = Template("<p>Hello {{name}}</p>", ["name"]);

        Result<RenderedMessage> rendered = template.Render(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = "Amira" });

        // The body is written by an administrator and may legitimately contain
        // markup; the values come from data and must not. Escaping both would
        // show people `&lt;p&gt;` in every message.
        Assert.Equal("<p>Hello Amira</p>", rendered.Value.Body);
    }

    [Fact]
    public void AnAmpersandIsEscapedOnceAndOnly()
    {
        NotificationTemplate template = Template("{{name}}", ["name"]);

        Result<RenderedMessage> rendered = template.Render(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = "R&D <x>" });

        // Order matters: escaping `<` before `&` would turn `&lt;` into
        // `&amp;lt;` and show the reader the escape sequence instead of the
        // character.
        Assert.Equal("R&amp;D &lt;x&gt;", rendered.Value.Body);
    }

    [Fact]
    public void OnlyArabicAndEnglishAreAccepted()
    {
        Result<NotificationTemplate> created = NotificationTemplate.Create(
            "test", "fr", "Bonjour", "Bonjour", [], Now);

        Assert.True(created.IsFailure);
        Assert.Equal("NOTIFICATIONS.UNSUPPORTED_LOCALE", created.Errors[0].Code);
    }

    [Fact]
    public void RevisingBumpsTheVersion()
    {
        NotificationTemplate template = Template("First", []);

        Assert.Equal(1, template.Version);
        Assert.True(template.Revise("New", "Second", [], Now).IsSuccess);

        // A delivery record says which version was sent, so a template revised
        // twice does not rewrite the history of every message it produced.
        Assert.Equal(2, template.Version);
        Assert.Equal("Second", template.Body);
    }

    [Fact]
    public void ARevisionThatWouldNotValidateChangesNothing()
    {
        NotificationTemplate template = Template("Hello {{name}}", ["name"]);

        Result revised = template.Revise("Hi", "Hello {{name}} of {{team}}", ["name"], Now);

        Assert.True(revised.IsFailure);
        Assert.Equal("Hello {{name}}", template.Body);
        Assert.Equal(1, template.Version);
    }

    private static NotificationTemplate Template(string body, IReadOnlyList<string> variables)
        => NotificationTemplate.Create("test", "en", "Subject", body, variables, Now).Value;
}

/// <summary>
/// The templates the Platform ships with.
/// <para>
/// <b>Every template exists in Arabic and English</b> is Phase 9's acceptance
/// criterion, and this is it — executed rather than asserted in a document. A
/// template added without its pair fails here, which is the only way that rule
/// survives contact with a deadline.
/// </para>
/// </summary>
public sealed class SeededTemplateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EveryTemplateExistsInBothLanguages()
    {
        var byCode = TemplateSeeder.Templates
            .GroupBy(t => t.Code, StringComparer.Ordinal)
            .ToList();

        var incomplete = byCode
            .Where(group => !group.Any(t => t.Locale == "ar")
                         || !group.Any(t => t.Locale == "en"))
            .Select(group => group.Key)
            .ToList();

        Assert.True(
            incomplete.Count == 0,
            "These templates do not exist in both languages:"
            + Environment.NewLine + string.Join(Environment.NewLine, incomplete));
    }

    [Fact]
    public void EverySeededTemplateIsValid()
    {
        var invalid = new List<string>();

        foreach (var seed in TemplateSeeder.Templates)
        {
            Result<NotificationTemplate> created = NotificationTemplate.Create(
                seed.Code, seed.Locale, seed.Subject, seed.Body, seed.Variables, Now);

            if (created.IsFailure)
            {
                invalid.Add($"{seed.Code}/{seed.Locale}: {created.Errors[0].Message}");
            }
        }

        // A seeded template that will not validate leaves the Platform unable to
        // send that message for as long as nobody notices — and nobody notices,
        // because the failure is a log line at startup.
        Assert.True(
            invalid.Count == 0,
            "These seeded templates are not valid:"
            + Environment.NewLine + string.Join(Environment.NewLine, invalid));
    }

    [Fact]
    public void BothLanguagesOfATemplateDeclareTheSameVariables()
    {
        var mismatched = TemplateSeeder.Templates
            .GroupBy(t => t.Code, StringComparer.Ordinal)
            .Where(group => group
                .Select(t => string.Join(',', t.Variables.Order(StringComparer.Ordinal)))
                .Distinct(StringComparer.Ordinal)
                .Count() > 1)
            .Select(group => group.Key)
            .ToList();

        // A caller supplies one set of values for both languages. If the Arabic
        // needs a variable the English does not, half the company gets a
        // refusal and the other half gets a message — which is the worst way to
        // discover a typo.
        Assert.True(
            mismatched.Count == 0,
            "These templates declare different variables per language:"
            + Environment.NewLine + string.Join(Environment.NewLine, mismatched));
    }
}
