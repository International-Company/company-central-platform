using System.Text.RegularExpressions;
using CCP.Modules.Workflow.Domain.Instances;

namespace CCP.Architecture.Tests;

/// <summary>
/// Every notification template is one the Platform sends, and every one it sends
/// exists.
/// <para>
/// <b>One was seeded and never sent.</b> "Two-factor authentication was turned
/// on for your account" existed in Arabic and English, editable in the portal,
/// and nothing could deliver it: the Security module published no integration
/// events, so no listener ever learned an enrolment happened. It is the message
/// that exposes a takeover — an intruder enrolling their own authenticator —
/// and its absence would have been noticed only by the person it failed to warn.
/// </para>
/// <para>
/// The opposite failure is as quiet: a listener naming a template that was never
/// seeded logs a warning and tells nobody anything.
/// </para>
/// </summary>
public sealed partial class NotificationTemplateCoverageTests
{
    private const string InstancePrefix = "workflow.instance.";

    [GeneratedRegex(@"new\(\s*""(?<code>[a-z0-9_.-]+)""\s*,\s*""(?:ar|en)""", RegexOptions.Compiled)]
    private static partial Regex SeededTemplate();

    /// <summary>A template code written as a literal in a <c>SendRequest</c>.</summary>
    [GeneratedRegex(@"new SendRequest\(\s*[^,]+,\s*""(?<code>[a-z0-9_.-]+)""", RegexOptions.Compiled)]
    private static partial Regex SentLiteral();

    /// <summary>A template code built by interpolation, such as the instance outcome.</summary>
    [GeneratedRegex(@"new SendRequest\(\s*[^,]+,\s*\$""(?<prefix>[a-z0-9_.-]+)\{", RegexOptions.Compiled)]
    private static partial Regex SentInterpolated();

    [Fact]
    public void EverySeededTemplateIsSent()
    {
        string listeners = Listeners();
        string[] literal = [.. SentLiteral().Matches(listeners).Select(m => m.Groups["code"].Value)];
        string[] prefixes = [.. SentInterpolated().Matches(listeners).Select(m => m.Groups["prefix"].Value)];

        string[] orphaned =
        [
            .. Seeded().Where(code =>
                !literal.Contains(code, StringComparer.Ordinal)
                && !prefixes.Any(prefix => code.StartsWith(prefix, StringComparison.Ordinal))),
        ];

        Assert.True(
            orphaned.Length == 0,
            "These templates are seeded and nothing sends them. They appear in the portal, can be "
            + "edited, and are never delivered. Add the listener, or remove the template."
            + Environment.NewLine
            + string.Join(Environment.NewLine, orphaned));
    }

    [Fact]
    public void EverySentTemplateIsSeeded()
    {
        HashSet<string> seeded = [.. Seeded()];

        string[] missing =
        [
            .. SentLiteral().Matches(Listeners())
                .Select(m => m.Groups["code"].Value)
                .Where(code => !seeded.Contains(code))
                .Distinct(),
        ];

        Assert.True(
            missing.Length == 0,
            "These templates are sent and were never seeded. The send logs a warning and nobody is "
            + "told anything."
            + Environment.NewLine
            + string.Join(Environment.NewLine, missing));
    }

    /// <summary>
    /// The outcome template is built from the instance's status, so the two sets
    /// must match exactly.
    /// <para>
    /// A status added without a template leaves every requester whose request
    /// ends that way uninformed; a template without a status is dead text.
    /// <c>Running</c> is not an outcome and has neither.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryInstanceOutcomeHasATemplate()
    {
        string[] outcomes =
        [
            .. Enum.GetNames<InstanceStatus>()
                .Where(name => name != nameof(InstanceStatus.Running))
                .Select(name => InstancePrefix + name.ToLowerInvariant())
                .Order(StringComparer.Ordinal),
        ];

        string[] templates =
        [
            .. Seeded()
                .Where(code => code.StartsWith(InstancePrefix, StringComparison.Ordinal))
                .Distinct()
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(outcomes, templates);
    }

    [Fact]
    public void TheScanSeesTemplatesAndListeners()
    {
        Assert.True(Seeded().Distinct().Count() >= 8);
        Assert.True(SentLiteral().Count(Listeners()) >= 4);
    }

    private static string[] Seeded()
        => [.. SeededTemplate()
            .Matches(File.ReadAllText(Path.Combine(NotificationsInfrastructure(), "TemplateSeeder.cs")))
            .Select(m => m.Groups["code"].Value)];

    private static string Listeners()
        => string.Join(
            "\n",
            Directory.EnumerateFiles(Path.Combine(NotificationsInfrastructure(), "Listeners"), "*.cs")
                .Select(File.ReadAllText));

    private static string NotificationsInfrastructure()
        => Path.Combine(
            RepositoryRoot(), "src", "Modules", "Notifications",
            "CCP.Modules.Notifications.Infrastructure");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
