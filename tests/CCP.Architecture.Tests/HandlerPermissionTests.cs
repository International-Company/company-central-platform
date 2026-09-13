using System.Text.RegularExpressions;
using CCP.Modules.Authorization.Application;

namespace CCP.Architecture.Tests;

/// <summary>
/// A permission checked inside a handler is one the Platform can grant.
/// <para>
/// The catalogue is derived from endpoint metadata, so a permission exists
/// only if an endpoint declares it. One evaluated inside a handler is therefore
/// never created unless it is listed in <see cref="HandlerPermissions"/>, which
/// the seeder reads — and a permission that is never created is one nobody can
/// hold, so every check against it answers "denied".
/// </para>
/// <para>
/// <b>That shipped.</b> Asking what access another user holds requires
/// <c>platform.authorization.inspect</c>, written as a literal in the handler.
/// The seeder's exception list did not contain it, so the endpoint answered 403
/// to everyone who asked about another user, the first administrator included,
/// while the authorization guide listed it as working.
/// </para>
/// </summary>
public sealed partial class HandlerPermissionTests
{
    /// <summary>
    /// A call to the resolver — preceded by a dot, which is what separates a
    /// call from the method's own declaration — up to the end of its statement.
    /// </summary>
    [GeneratedRegex(
        @"\.\s*Evaluate(?:ForApplication|Delegated)?Async\s*\((?<arguments>[^;]*);",
        RegexOptions.Compiled)]
    private static partial Regex ResolverCall();

    [GeneratedRegex(@"""platform\.[a-z0-9.-]+""", RegexOptions.Compiled)]
    private static partial Regex PermissionLiteral();

    [Fact]
    public void NoHandlerEvaluatesAPermissionByLiteral()
    {
        var literal = new List<string>();

        foreach ((string path, string source) in SourceFiles())
        {
            foreach (Match call in ResolverCall().Matches(source))
            {
                Match permission = PermissionLiteral().Match(call.Groups["arguments"].Value);

                if (permission.Success)
                {
                    literal.Add($"{Path.GetFileName(path)}: {permission.Value}");
                }
            }
        }

        Assert.True(
            literal.Count == 0,
            "These handlers evaluate a permission written as a literal. The permission catalogue "
            + "is derived from endpoints, so one checked inside a handler is never created and "
            + "nobody can ever hold it — every check answers denied. Add it to HandlerPermissions, "
            + "which the seeder catalogues, and evaluate it from there."
            + Environment.NewLine
            + string.Join(Environment.NewLine, literal));
    }

    /// <summary>
    /// Every handler permission is actually checked somewhere.
    /// <para>
    /// The opposite failure, and the one the catalogue was derived from
    /// endpoints to prevent: a permission an administrator can grant that
    /// controls nothing. Granting it looks like a decision and changes nothing.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryHandlerPermissionIsChecked()
    {
        string[] names = [nameof(HandlerPermissions.ActOnBehalf), nameof(HandlerPermissions.InspectOthersAccess)];

        Assert.Equal(names.Length, HandlerPermissions.All.Count);

        string elsewhere = string.Join(
            "\n",
            SourceFiles()
                .Where(file => !file.Path.EndsWith("/HandlerPermissions.cs", StringComparison.Ordinal))
                .Select(file => file.Source));

        string[] neverChecked =
        [
            .. names.Where(name =>
                !Regex.IsMatch(elsewhere, $@"HandlerPermissions\.{name}\b")),
        ];

        Assert.True(
            neverChecked.Length == 0,
            "These handler permissions are catalogued and granted but nothing checks them: "
            + string.Join(", ", neverChecked));
    }

    /// <summary>
    /// Proves the scan still finds the resolver's callers.
    /// <para>
    /// Both assertions above are "nothing was wrong", which is also what they
    /// say once the pattern stops matching. There are at least two calls: the
    /// token endpoint's delegation check and the inspect check.
    /// </para>
    /// </summary>
    [Fact]
    public void TheScanFindsTheResolverCalls()
    {
        int calls = SourceFiles().Sum(file => ResolverCall().Count(file.Source));

        Assert.True(calls >= 2, $"Only {calls} resolver call(s) were found; the scan has broken.");
    }

    private static IEnumerable<(string Path, string Source)> SourceFiles()
    {
        string root = Path.Combine(RepositoryRoot(), "src");

        foreach (string path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string normalised = path.Replace('\\', '/');

            if (normalised.Contains("/obj/", StringComparison.Ordinal)
                || normalised.Contains("/bin/", StringComparison.Ordinal))
            {
                continue;
            }

            yield return (normalised, File.ReadAllText(path));
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
