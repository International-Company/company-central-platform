using System.Text.RegularExpressions;

namespace CCP.Architecture.Tests;

/// <summary>
/// Every outbound client in the Platform is accounted for.
/// <para>
/// <b>"Every outbound call passes one door" was true of the calls somebody
/// remembered.</b> The email channel went round it for nine phases (#33, #45)
/// and the breached-password checker went round it for longer — and both were
/// found by reading, one at a time, long after they were written. A rule
/// enforced by memory is a rule that holds until somebody new adds a feature.
/// </para>
/// <para>
/// So this counts them. A client that opens a socket to somewhere outside the
/// Platform has to appear below with a reason, and adding a fourth means adding
/// a line here — which is the point: the decision becomes something somebody
/// recorded rather than a diff nobody read.
/// </para>
/// <para>
/// <b>It does not check that a client is governed</b>, and could not: the
/// governing is a call at the top of a method, not a shape a regular expression
/// can see. What it checks is that nobody adds one quietly.
/// </para>
/// </summary>
public sealed partial class OutboundCoverageTests
{
    /// <summary>
    /// Every way the Platform builds something that can reach the internet.
    /// <para>
    /// Not an exhaustive taxonomy of the BCL. These are the three the Platform
    /// actually uses, and a fourth kind arriving is exactly the event this test
    /// exists to notice — it will not match, the reviewed set will still be
    /// complete, and the test will pass while a new door opens. That is the
    /// honest limit of a source scan, and the reason the sibling assertion
    /// below checks the scanner still sees anything at all.
    /// </para>
    /// </summary>
    [GeneratedRegex(
        @"AddHttpClient(?!Instrumentation)|new\s+HttpClient\s*\(|new\s+SmtpClient\s*\(",
        RegexOptions.Compiled)]
    private static partial Regex OutboundClient();

    /// <summary>
    /// The outbound clients that exist, and why each is allowed to.
    /// <para>
    /// Keyed by the file, because that is what a failure can point at.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> Reviewed = new(StringComparer.Ordinal)
    {
        ["CCP.Modules.Integrations.Infrastructure/IntegrationInfrastructureRegistration.cs"] =
            "The governed door itself. Its primary handler is GuardedConnect, so every socket it "
            + "opens is checked against the allow-list and connected to the address that was checked.",

        ["CCP.Modules.Notifications.Infrastructure/Channels/Providers.cs"] =
            "SMTP, which the connector cannot carry. It asks IOutboundGateway to approve the mail "
            + "host before dialling and records every attempt in the shared call log (#33, #45).",

        ["CCP.Modules.Identity.Infrastructure/IdentityInfrastructureRegistration.cs"] =
            "Breached-password screening, off by default. It asks IOutboundGateway to approve the "
            + "range API's host before calling and records the attempt, the same way SMTP does.",
    };

    [Fact]
    public void EveryOutboundClientIsReviewed()
    {
        var unreviewed = new List<string>();

        foreach ((string path, string source) in SourceFiles())
        {
            if (!OutboundClient().IsMatch(source))
            {
                continue;
            }

            if (!Reviewed.Keys.Any(reviewed => path.EndsWith(reviewed, StringComparison.Ordinal)))
            {
                unreviewed.Add(path);
            }
        }

        Assert.True(
            unreviewed.Count == 0,
            "These build an outbound client and are not in the reviewed set. Every outbound call "
            + "passes one door (ARCHITECTURE.md §19.1): either route it through the integration "
            + "connector, or ask IOutboundGateway to approve the host and record the attempt. Then "
            + "add it here with the reason, so the decision is recorded rather than assumed."
            + Environment.NewLine
            + string.Join(Environment.NewLine, unreviewed));
    }

    /// <summary>
    /// The reviewed set contains nothing that has gone away.
    /// <para>
    /// A list of exceptions outlives the code it excuses. An entry for a file
    /// that no longer builds a client is an excuse standing open for whatever
    /// gets written there next.
    /// </para>
    /// </summary>
    [Fact]
    public void TheReviewedSetHasNoLeftovers()
    {
        var stale = new List<string>();

        foreach (string reviewed in Reviewed.Keys)
        {
            bool found = SourceFiles().Any(file =>
                file.Path.EndsWith(reviewed, StringComparison.Ordinal)
                && OutboundClient().IsMatch(file.Source));

            if (!found)
            {
                stale.Add(reviewed);
            }
        }

        Assert.True(
            stale.Count == 0,
            "These are excused from the outbound rule and no longer build a client. Remove them, "
            + "so the exception does not stand open for whatever is written there next."
            + Environment.NewLine
            + string.Join(Environment.NewLine, stale));
    }

    /// <summary>
    /// Proves the scan can still see anything at all.
    /// <para>
    /// A rule expressed as "nothing unreviewed matched" passes for ever the
    /// moment the matching stops working — a moved directory, a renamed
    /// project, a regular expression edited carelessly. This is the assertion
    /// that fails instead.
    /// </para>
    /// </summary>
    [Fact]
    public void TheScanActuallySeesTheSource()
    {
        (string Path, string Source)[] files = [.. SourceFiles()];

        Assert.True(
            files.Length > 200,
            $"Only {files.Length} source files were scanned. The scan has lost the source tree, so "
            + "the assertions above are passing on an empty set.");

        Assert.True(
            files.Count(file => OutboundClient().IsMatch(file.Source)) >= Reviewed.Count,
            "Fewer outbound clients were recognised than are reviewed. The pattern has stopped "
            + "matching what it is meant to match.");
    }

    private static IEnumerable<(string Path, string Source)> SourceFiles()
    {
        string root = Path.Combine(RepositoryRoot(), "src");

        foreach (string path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            // Build output carries copies of the source's own strings and would
            // report the same file twice under a path nobody edits.
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            {
                continue;
            }

            yield return (path.Replace('\\', '/'), File.ReadAllText(path));
        }
    }

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
