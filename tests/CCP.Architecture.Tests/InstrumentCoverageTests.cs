using System.Reflection;
using CCP.Kernel.Application.Observability;

namespace CCP.Architecture.Tests;

/// <summary>
/// Every instrument the Platform declares is emitted by something.
/// <para>
/// <b>This exists because one of them was not, for a whole phase.</b>
/// <c>ccp.jobs.runs</c> was declared in Phase 14 with a unit, a description and
/// an alert written against it in the runbook — and
/// <see cref="PlatformMetrics.BackgroundJobRan"/> was placed in an assembly that
/// no module's Infrastructure project references, which is where all five
/// background sweeps live. So the only code with any reason to call it could
/// not. Nothing failed to compile, because nothing tried. The alert watched a
/// counter structurally incapable of moving, and therefore read exactly the same
/// whether the sweeps were running perfectly or had stopped in the night.
/// </para>
/// <para>
/// <b>A missing call is invisible to every other kind of test.</b> There is no
/// assertion to fail and no exception to catch: the metric simply reports
/// nothing, which is indistinguishable from a system where nothing has happened
/// yet. Only something structural notices, and this is it.
/// </para>
/// <para>
/// <b>What it checks, and what it cannot.</b> It searches the source for a call
/// to each recording method outside the class that declares it. It cannot prove
/// the call is on a path that ever runs — reflection and text both stop short of
/// that, and a test claiming otherwise would reassure without checking. What it
/// does catch is the failure that actually happened: an instrument with no
/// caller anywhere in the Platform.
/// </para>
/// </summary>
public sealed class InstrumentCoverageTests
{
    private static readonly string RepositoryRoot = LocateRepositoryRoot();

    /// <summary>
    /// The file that declares the instruments, excluded from the search.
    /// <para>
    /// Its own declarations would otherwise satisfy the test, which would make
    /// it pass in exactly the situation it exists to fail.
    /// </para>
    /// </summary>
    private const string DeclaringFile = "PlatformMetrics.cs";

    [Fact]
    public void EveryDeclaredInstrumentIsEmittedBySomething()
    {
        IReadOnlyList<string> recorders = RecordingMethods();

        // If this ever finds nothing, the reflection below has stopped matching
        // the class rather than the class having no instruments — and a test
        // that silently checks nothing is worse than no test.
        Assert.NotEmpty(recorders);

        string[] sources = PlatformSources();

        List<string> unemitted =
            [.. recorders.Where(method => !sources.Any(source => Calls(source, method)))];

        Assert.True(
            unemitted.Count == 0,
            "These instruments are declared and nothing calls them, so whatever is "
            + "alerting on them is reporting health it cannot know: "
            + string.Join(", ", unemitted)
            + ". Either emit them where the measured work happens, or remove the "
            + "instrument and its alert.");
    }

    /// <summary>
    /// The public methods that record a measurement.
    /// <para>
    /// Taken from the type rather than listed here. A hand-written list of
    /// instruments to check the instruments against would need the same
    /// discipline that failed in the first place.
    /// </para>
    /// </summary>
    private static IReadOnlyList<string> RecordingMethods() =>
        [.. typeof(PlatformMetrics)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Where(method => method.Name != nameof(IDisposable.Dispose))
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    /// <summary>Every source file the Platform ships, less the declaring one.</summary>
    private static string[] PlatformSources() =>
        [.. Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !Path.GetFileName(path).Equals(DeclaringFile, StringComparison.Ordinal))
            .Select(File.ReadAllText)];

    /// <summary>
    /// Whether a source file calls the named method.
    /// <para>
    /// The open parenthesis is required, so a mention in a comment or in a
    /// documentation cross-reference does not count as a call. That distinction
    /// is the whole point: the defect this catches was a method that everything
    /// referred to and nothing invoked.
    /// </para>
    /// </summary>
    private static bool Calls(string source, string method) =>
        source.Contains($".{method}(", StringComparison.Ordinal);

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
