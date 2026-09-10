using System.Reflection;
using CCP.Kernel.Application.Configuration;

namespace CCP.Architecture.Tests;

/// <summary>
/// Every setting the Platform declares is read by something, and everything it
/// reads is declared.
/// <para>
/// <b>The same guard as <see cref="InstrumentCoverageTests"/>, for the same
/// reason.</b> Two metrics were declared with alerts written against them and no
/// caller anywhere, and both read as permanently healthy because a metric nobody
/// emits is indistinguishable from a system that is fine. A setting has the
/// identical failure shape and a worse consequence: it appears on the
/// Configuration screen, an administrator changes it to solve a problem, the
/// value is stored and audited, and absolutely nothing happens. There is no
/// error to see and no way to tell it apart from a change that did not help.
/// </para>
/// <para>
/// The reverse direction matters too. A key read but never declared is a setting
/// that can never be set: the reader falls back for ever and the screen has no
/// row for it, so the ability to change it exists only in the code's imagination.
/// </para>
/// </summary>
public sealed class SettingCoverageTests
{
    private static readonly string RepositoryRoot = LocateRepositoryRoot();

    /// <summary>
    /// Where the keys are declared to the Configuration module. Excluded from
    /// the search for readers, since listing a key is not reading it.
    /// </summary>
    private const string SeederFile = "PlatformSettingSeeder.cs";

    /// <summary>The file holding the constants themselves.</summary>
    private const string KeysFile = "IPlatformSettings.cs";

    [Fact]
    public void EveryDeclaredSettingIsReadBySomething()
    {
        IReadOnlyList<string> keys = DeclaredKeys();

        // A guard that silently checks nothing is worse than no guard, and this
        // project has shipped that twice.
        Assert.NotEmpty(keys);

        string[] sources = PlatformSources(excluding: [SeederFile, KeysFile]);

        List<string> unread =
            [.. keys.Where(key => !sources.Any(source => Mentions(source, key)))];

        Assert.True(
            unread.Count == 0,
            "These settings are declared and nothing reads them. An administrator "
            + "can change one, see it stored and audited, and have nothing at all "
            + "happen: "
            + string.Join(", ", unread)
            + ". Either read it where the behaviour lives, or stop declaring it.");
    }

    [Fact]
    public void EverySettingTheSeederDeclaresIsOneOfTheKeys()
    {
        // The seeder builds its list from the same constants, so this asserts
        // it has not started passing a literal -- which would declare a key
        // nothing reads while the test above still passed.
        string seeder = SourceOf(SeederFile);

        Assert.DoesNotContain("\"platform.", seeder, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every key the Platform reads is one it also declares.
    /// <para>
    /// Enforced by there being exactly one place the names live: a reader that
    /// passed a string literal instead of a constant could name a setting the
    /// seeder never declares, and would then fall back for ever with no row on
    /// the screen to explain why.
    /// </para>
    /// </summary>
    [Fact]
    public void NoSettingIsReadByALiteralName()
    {
        var offenders = new List<string>();

        foreach ((string path, string source) in PlatformFiles(
                     excluding: [SeederFile, KeysFile]))
        {
            if (source.Contains("GetDurationAsync(\"platform.", StringComparison.Ordinal)
                || source.Contains("GetIntegerAsync(\"platform.", StringComparison.Ordinal)
                || source.Contains("GetBooleanAsync(\"platform.", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(path));
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These read a setting by a literal name rather than through "
            + "PlatformSettingKeys, so nothing can tell whether it is declared: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// The key constants, read off the type rather than listed here. A list of
    /// settings to check the settings against needs the same discipline that
    /// fails in the first place.
    /// </summary>
    private static IReadOnlyList<string> DeclaredKeys() =>
        [.. typeof(PlatformSettingKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)

            // The namespace itself is not a setting; it is the prefix they all
            // share.
            .Where(value => value != PlatformSettingKeys.Namespace)];

    /// <summary>
    /// Whether a source file names this setting, by its constant.
    /// <para>
    /// Matched on the constant's member name rather than the key string,
    /// because a call site writes <c>PlatformSettingKeys.JobHistoryRetention</c>
    /// and never the text.
    /// </para>
    /// </summary>
    private static bool Mentions(string source, string key)
    {
        string member = MemberFor(key);

        return source.Contains($"PlatformSettingKeys.{member}", StringComparison.Ordinal);
    }

    private static string MemberFor(string key) =>
        typeof(PlatformSettingKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .First(field => field.IsLiteral && (string?)field.GetRawConstantValue() == key)
            .Name;

    private static string[] PlatformSources(string[] excluding) =>
        [.. PlatformFiles(excluding).Select(entry => entry.Source)];

    private static IEnumerable<(string Path, string Source)> PlatformFiles(string[] excluding) =>
        Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !excluding.Contains(Path.GetFileName(path), StringComparer.Ordinal))
            .Select(path => (path, File.ReadAllText(path)));

    private static string SourceOf(string fileName) =>
        File.ReadAllText(
            Directory
                .EnumerateFiles(Path.Combine(RepositoryRoot, "src"), fileName, SearchOption.AllDirectories)
                .Single(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)));

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
