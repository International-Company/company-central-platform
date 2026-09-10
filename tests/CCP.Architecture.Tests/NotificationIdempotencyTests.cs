using System.Reflection;
using System.Text.RegularExpressions;

namespace CCP.Architecture.Tests;

/// <summary>
/// Every notification sent in response to an event says which event.
/// <para>
/// <b>Outbox delivery is at-least-once by design.</b> A crash between
/// dispatching a message and marking it processed causes a redelivery, which is
/// the right trade because the alternative loses events. The cost is that a
/// listener can be handed the same event twice, and a <c>SendRequest</c> that
/// does not carry <c>CausedBy</c> has nothing to recognise the second time by —
/// so it writes a second copy into somebody's inbox.
/// </para>
/// <para>
/// <b>This test exists because that is exactly what happened while writing the
/// fix.</b> Five sends were changed to carry the event id; four of them
/// actually did. The fifth had an explicit channel list in the way, so it did
/// not match, and it was the password reset — the most sensitive message the
/// Platform sends. Nothing failed. Nothing could have failed: a missing
/// deduplication key is silent until the day an event is redelivered.
/// </para>
/// </summary>
public sealed partial class NotificationIdempotencyTests
{
    private static readonly string RepositoryRoot = LocateRepositoryRoot();

    [Fact]
    public void EverySendFromAListenerCarriesTheEventItAnswers()
    {
        var offenders = new List<string>();
        int checkedSends = 0;

        foreach ((string path, string source) in ListenerSources())
        {
            foreach (Match send in SendRequests().Matches(source))
            {
                checkedSends++;

                if (!send.Value.Contains("CausedBy", StringComparison.Ordinal))
                {
                    offenders.Add(
                        $"{Path.GetFileName(path)}: a SendRequest with no CausedBy");
                }
            }
        }

        // A guard that silently checks nothing is worse than no guard, and this
        // project has shipped that twice.
        Assert.True(checkedSends >= 5, $"Only {checkedSends} sends were examined.");

        Assert.True(
            offenders.Count == 0,
            "These notifications are sent in response to an event and do not say which. "
            + "A redelivered event will produce a second copy in somebody's inbox, and "
            + "nothing anywhere will report it: "
            + string.Join("; ", offenders));
    }

    /// <summary>
    /// The listener files — the only place a notification is sent in answer to
    /// an event. A send made directly by a handler has no event behind it and
    /// nothing to deduplicate against.
    /// </summary>
    private static IEnumerable<(string Path, string Source)> ListenerSources()
    {
        string listeners = Path.Combine(
            RepositoryRoot,
            "src", "Modules", "Notifications",
            "CCP.Modules.Notifications.Infrastructure", "Listeners");

        Assert.True(Directory.Exists(listeners), $"No listeners directory at {listeners}.");

        return Directory
            .EnumerateFiles(listeners, "*.cs", SearchOption.AllDirectories)
            .Select(path => (path, File.ReadAllText(path)));
    }

    /// <summary>
    /// A <c>new SendRequest(...)</c> and everything up to the closing
    /// parenthesis of the <c>SendAsync</c> call it sits in.
    /// <para>
    /// Matched lazily up to <c>cancellationToken</c>, which every one of these
    /// calls ends with. Crude, and it does not need to be clever: the question
    /// is only whether the event id appears anywhere in the request being
    /// built.
    /// </para>
    /// </summary>
    [GeneratedRegex(@"new SendRequest\(.*?cancellationToken", RegexOptions.Singleline)]
    private static partial Regex SendRequests();

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CompanyCentralPlatform.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root could not be located.");
    }
}
