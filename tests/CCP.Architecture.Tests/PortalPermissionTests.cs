using System.Text.RegularExpressions;
using CCP.Modules.Authorization.Application;

namespace CCP.Architecture.Tests;

/// <summary>
/// Every permission the portal gates on is one the Platform can grant.
/// <para>
/// The portal hides a screen or a button unless the signed-in user holds a named
/// permission. A name the Platform never declares is held by nobody, so the
/// thing behind it disappears for everyone — the first administrator included —
/// and nothing fails: the page renders, just without it.
/// </para>
/// <para>
/// <b>The backend already shipped this once.</b> Asking about another user's
/// access required a permission no role could ever hold. When this test was
/// written the portal's ten names were all correct; it exists so a typo in the
/// eleventh is a failed build instead of a missing button somebody reports weeks
/// later.
/// </para>
/// </summary>
public sealed partial class PortalPermissionTests
{
    /// <summary>
    /// <c>usePermission('x')</c> and <c>permission="x"</c> — the two ways the
    /// portal asks. Deliberately not every <c>platform.*</c> string: setting keys
    /// share the prefix and are not permissions.
    /// </summary>
    [GeneratedRegex(
        @"(?:usePermission\(\s*['""]|permission=\{?\s*['""])(?<name>platform\.[a-z0-9_.-]+)['""]",
        RegexOptions.Compiled)]
    private static partial Regex PortalGate();

    [GeneratedRegex(@"RequirePermissionAttribute\(""(?<name>platform\.[a-z0-9_.-]+)""\)", RegexOptions.Compiled)]
    private static partial Regex EndpointPermission();

    [GeneratedRegex(@"const string \w+ = ""(?<name>platform\.[a-z0-9_.-]+)""", RegexOptions.Compiled)]
    private static partial Regex PermissionConstant();

    [Fact]
    public void EveryPortalGateNamesAGrantablePermission()
    {
        HashSet<string> grantable = Grantable();

        string[] unknown =
        [
            .. PortalGates()
                .Where(gate => !grantable.Contains(gate.Name))
                .Select(gate => $"{gate.Name}  ({gate.File})")
                .Distinct(),
        ];

        Assert.True(
            unknown.Length == 0,
            "The portal gates on permissions the Platform never declares. Nobody can hold them, so "
            + "whatever they guard is hidden from every user, administrators included."
            + Environment.NewLine
            + string.Join(Environment.NewLine, unknown));
    }

    /// <summary>
    /// Proves both sides are still being read.
    /// <para>
    /// Ten portal gates and thirty-eight declared permissions when this was
    /// written. A pattern that stops matching turns the check above into a pass.
    /// </para>
    /// </summary>
    [Fact]
    public void TheScanSeesBothSides()
    {
        Assert.True(PortalGates().Select(gate => gate.Name).Distinct().Count() >= 5);
        Assert.True(Grantable().Count >= 30);
    }

    /// <summary>
    /// What the seeder catalogues: every permission an endpoint requires, the
    /// constants those requirements are written with, and the handler list.
    /// </summary>
    private static HashSet<string> Grantable()
    {
        string source = string.Join("\n", Files(Path.Combine(RepositoryRoot(), "src"), "*.cs"));

        return
        [
            .. EndpointPermission().Matches(source).Select(match => match.Groups["name"].Value),
            .. PermissionConstant().Matches(source).Select(match => match.Groups["name"].Value),
            .. HandlerPermissions.All,
        ];
    }

    private static IEnumerable<(string Name, string File)> PortalGates()
    {
        string root = Path.Combine(RepositoryRoot(), "frontend", "src");

        foreach (string path in Directory.EnumerateFiles(root, "*.ts*", SearchOption.AllDirectories))
        {
            if (path.Contains("node_modules", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in PortalGate().Matches(File.ReadAllText(path)))
            {
                yield return (match.Groups["name"].Value, Path.GetFileName(path));
            }
        }
    }

    private static IEnumerable<string> Files(string root, string pattern)
    {
        foreach (string path in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
        {
            string normalised = path.Replace('\\', '/');

            if (!normalised.Contains("/obj/", StringComparison.Ordinal)
                && !normalised.Contains("/bin/", StringComparison.Ordinal))
            {
                yield return File.ReadAllText(path);
            }
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
