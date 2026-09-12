using System.Text.RegularExpressions;
using CCP.Kernel.Api.Security;
using CCP.Modules.Identity.Domain.Users;

namespace CCP.Architecture.Tests;

/// <summary>
/// Every error the Platform declares is an error the Platform can return.
/// <para>
/// <b>Seventeen were not.</b> They were found by listing every <c>Error</c>
/// value nothing references, after one of them turned up by accident:
/// <c>SecurityErrors.MfaRequiredByPolicy</c> carried a comment describing what
/// the client shows "when the reason is a missing factor rather than a lapsed
/// one", and nothing raised it — so somebody with no second factor was told to
/// confirm the second factor they did not have.
/// </para>
/// <para>
/// The other sixteen were checked one at a time and none marked a missing
/// check. They were <b>rejected designs</b>: written when the shape was imagined
/// one way and left behind when it went another. Token issuance answers
/// <c>InvalidClient</c> rather than "the application is disabled", so a caller
/// cannot tell a disabled client from a wrong secret. Sensitive settings are
/// redacted on read rather than refused. Re-declaring a setting updates it
/// instead of conflicting.
/// </para>
/// <para>
/// <b>Leaving them was not harmless.</b> An error nothing raises is a claim the
/// Platform makes and does not honour, and the next person in one of those
/// handlers may reach for the error sitting right there — restoring the design
/// that was rejected, believing they are fixing an oversight.
/// </para>
/// </summary>
public sealed partial class ErrorCatalogueTests
{
    [GeneratedRegex(
        @"public static (?:readonly Error|Error) (\w+)",
        RegexOptions.Compiled)]
    private static partial Regex Declaration();

    /// <summary>
    /// Errors that are declared, never raised, and allowed to be.
    /// <para>
    /// One entry, and it has to be here rather than deleted: the code is part
    /// of the response contract, written by a kernel middleware that cannot
    /// reference a module. <see cref="TheReviewedSetHasNoLeftovers"/> refuses
    /// an entry that has started being raised, because an exception outlives
    /// the thing it excuses.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> Reviewed = new(StringComparer.Ordinal)
    {
        ["PasswordChangeRequired"] =
            "Raised by no handler, because PasswordChangePendingMiddleware refuses in the kernel "
            + "before any endpoint runs and writes the code itself. The value stays as the "
            + "module's record of the code; TheKernelAndIdentityAgreeOnThePasswordCode keeps the "
            + "two spellings identical.",
    };

    [Fact]
    public void EveryDeclaredErrorIsRaisedSomewhere()
    {
        (string Path, string Source)[] sources = [.. SourceFiles()];
        string everything = string.Join("\n", sources.Select(file => file.Source));

        var unraised = new List<string>();

        foreach ((string path, string source) in sources)
        {
            if (!path.EndsWith("Errors.cs", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in Declaration().Matches(source))
            {
                string name = match.Groups[1].Value;

                if (Reviewed.ContainsKey(name))
                {
                    continue;
                }

                // Once for the declaration itself; anything more is a use.
                int references = Regex.Count(everything, $@"\b{Regex.Escape(name)}\b");

                if (references <= 1)
                {
                    unraised.Add($"{name}  ({Path.GetFileName(path)})");
                }
            }
        }

        Assert.True(
            unraised.Count == 0,
            "These errors are declared and nothing returns them. Either the check they describe is "
            + "missing — in which case write it — or the design went another way and the error is a "
            + "claim the Platform does not honour, which the next person to touch that handler may "
            + "reach for and restore. Delete it, or record it in the reviewed set with the reason."
            + Environment.NewLine
            + string.Join(Environment.NewLine, unraised));
    }

    /// <summary>
    /// The reviewed set contains nothing that is now raised, or now gone.
    /// </summary>
    [Fact]
    public void TheReviewedSetHasNoLeftovers()
    {
        string everything = string.Join("\n", SourceFiles().Select(file => file.Source));

        var stale = new List<string>();

        foreach (string name in Reviewed.Keys)
        {
            int references = Regex.Count(everything, $@"\b{Regex.Escape(name)}\b");

            if (references == 0)
            {
                stale.Add($"{name} no longer exists");
            }
            else if (references > 1)
            {
                stale.Add($"{name} is now raised, so it does not need excusing");
            }
        }

        Assert.True(
            stale.Count == 0,
            "The reviewed set is out of date." + Environment.NewLine
            + string.Join(Environment.NewLine, stale));
    }

    /// <summary>
    /// The two spellings of the password-change refusal stay the same spelling.
    /// <para>
    /// The kernel refuses before any endpoint runs and writes the body itself,
    /// so it cannot ask a module for the code (§6.2). A value in two places
    /// goes stale in one of them, and here it would go quietly: a client
    /// matching the documented code would stop recognising the refusal and
    /// nothing would fail.
    /// </para>
    /// </summary>
    [Fact]
    public void TheKernelAndIdentityAgreeOnThePasswordCode()
    {
        Assert.Equal(
            PasswordChangePendingMiddleware.RefusalCode,
            IdentityErrors.PasswordChangeRequired.Code);
    }

    /// <summary>
    /// Proves the scan still sees the catalogue.
    /// <para>
    /// "Nothing was unraised" is what this says when it has stopped reading the
    /// source at all. The Platform has had more than two hundred declared
    /// errors since Phase 6.
    /// </para>
    /// </summary>
    [Fact]
    public void TheScanSeesTheCatalogue()
    {
        int declared = SourceFiles()
            .Where(file => file.Path.EndsWith("Errors.cs", StringComparison.Ordinal))
            .Sum(file => Declaration().Count(file.Source));

        Assert.True(
            declared > 200,
            $"Only {declared} error declarations were found, so the scan has lost the source.");
    }

    private static IEnumerable<(string Path, string Source)> SourceFiles()
    {
        string root = Path.Combine(RepositoryRoot(), "src");

        foreach (string path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
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
