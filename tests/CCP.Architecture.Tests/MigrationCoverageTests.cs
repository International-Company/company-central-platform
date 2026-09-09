using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace CCP.Architecture.Tests;

/// <summary>
/// Every module schema is migrated, by the host and by the test factory.
/// <para>
/// <b>Both lists are hand-written, and both have already been wrong.</b> The
/// test factory once migrated only the kernel, which hid a defect for three
/// phases; the host's list and the factory's list then each missed the Workflow
/// module the day it was added, and the symptom was three integration tests
/// answering 500 with no clue why.
/// </para>
/// <para>
/// The lists cannot easily be generated — one is a composition-root decision
/// about ordering, the other runs before any host exists — so they are checked
/// instead. Adding a module is now a change that fails loudly until both places
/// know about it, rather than one that fails obscurely much later.
/// </para>
/// </summary>
public sealed class MigrationCoverageTests
{
    private static readonly string RepositoryRoot = LocateRepositoryRoot();

    /// <summary>
    /// Every <see cref="DbContext"/> the Platform defines, found by loading the
    /// infrastructure assemblies rather than by listing them here — a list of
    /// contexts to check the lists of contexts against would have the same
    /// problem it is meant to solve.
    /// </summary>
    private static IReadOnlyList<string> PlatformContexts()
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);

        string binary = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

        foreach (string path in Directory.GetFiles(binary, "CCP.*.Infrastructure.dll"))
        {
            Assembly assembly;

            try
            {
                assembly = Assembly.LoadFrom(path);
            }
            catch (BadImageFormatException)
            {
                continue;
            }

            foreach (Type type in assembly.GetTypes())
            {
                if (type.IsSubclassOf(typeof(DbContext)) && !type.IsAbstract)
                {
                    found.Add(type.Name);
                }
            }
        }

        return [.. found];
    }

    [Fact]
    public void TheHostMigratesEveryModuleSchema()
        => AssertCovered(
            Path.Combine(RepositoryRoot, "src", "Host", "CCP.Api.Host", "Program.cs"),
            "the host's startup migrator");

    [Fact]
    public void TheIntegrationTestFactoryMigratesEveryModuleSchema()
        => AssertCovered(
            Path.Combine(
                RepositoryRoot, "tests", "CCP.Api.IntegrationTests", "PlatformApiFactory.cs"),
            "the integration test factory");

    private static void AssertCovered(string file, string what)
    {
        Assert.True(File.Exists(file), $"{file} was not found.");

        string source = File.ReadAllText(file);

        IReadOnlyList<string> contexts = PlatformContexts();

        Assert.NotEmpty(contexts);

        var missing = contexts
            .Where(name => !source.Contains(name, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"These schemas are never migrated by {what}, so their tables do not exist "
            + "and every request touching them answers 500:"
            + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

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
