using System.Text.RegularExpressions;

namespace CCP.Architecture.Tests;

/// <summary>
/// Every integration event the Platform declares is one it can actually send.
/// <para>
/// Business systems subscribe to event types by webhook. An event nothing
/// stages is a subscription that waits for ever with nothing to say why, and
/// the subscription is accepted without complaint.
/// </para>
/// <para>
/// <b>Five were like that.</b> Two were declared and never constructed:
/// <c>authz.role.created</c> and <c>authz.application.registered</c>. Three were
/// raised by their aggregates and discarded: a role gaining or losing a
/// permission, and an account being locked out. <c>Entity</c> said raised
/// events were "collected by the unit of work"; nothing collected them.
/// Organization had written its own loop and published correctly, which is why
/// the gap was confined to the two modules that trusted the comment.
/// </para>
/// </summary>
public sealed partial class IntegrationEventPublicationTests
{
    [GeneratedRegex(
        @"sealed record (?<name>\w+)\s*\([^{]*?\{\s*public override string EventType\s*=>\s*""(?<type>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex Declaration();

    [GeneratedRegex(@"Raise\(\s*new\s+(?<name>\w+)\s*\(", RegexOptions.Compiled)]
    private static partial Regex RaisedInAggregate();

    [Fact]
    public void EveryDeclaredEventIsConstructedSomewhere()
    {
        (string Path, string Source)[] sources = [.. SourceFiles()];
        string everything = string.Join("\n", sources.Select(file => file.Source));

        string[] never =
        [
            .. Declared(sources)
                .Where(e => !Regex.IsMatch(everything, $@"new\s+{Regex.Escape(e.Name)}\s*\("))
                .Select(e => $"{e.Type}  ({e.Name})"),
        ];

        Assert.True(
            never.Length == 0,
            "These integration events are declared and nothing ever constructs them. A business "
            + "system can subscribe to them and will never receive one. Raise them where the thing "
            + "they describe happens, or delete them."
            + Environment.NewLine
            + string.Join(Environment.NewLine, never));
    }

    /// <summary>
    /// A module whose aggregates raise events stages them.
    /// <para>
    /// <b>The honest limit:</b> this proves the module calls
    /// <c>EnqueueRaisedEventsAsync</c> somewhere, not that every handler which
    /// changes an aggregate does. A per-path proof is not something a source
    /// scan can give. What it does catch is the failure that shipped — a whole
    /// module raising events into a list nobody reads.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryModuleThatRaisesEventsStagesThem()
    {
        (string Path, string Source)[] sources = [.. SourceFiles()];
        string everything = string.Join("\n", sources.Select(file => file.Source));
        HashSet<string> declared = [.. Declared(sources).Select(e => e.Name)];

        var silent = new List<string>();

        foreach (IGrouping<string, (string Path, string Source)> module in sources
                     .Where(file => file.Path.Contains("/src/Modules/", StringComparison.Ordinal))
                     .GroupBy(file => ModuleOf(file.Path)))
        {
            // Events raised in this module's aggregates that no handler stages
            // explicitly by constructing its own copy.
            string[] onlyRaised =
            [
                .. module
                    .SelectMany(file => RaisedInAggregate().Matches(file.Source))
                    .Select(match => match.Groups["name"].Value)
                    .Where(declared.Contains)
                    .Distinct()
                    .Where(name => Regex.Count(everything, $@"new\s+{Regex.Escape(name)}\s*\(")
                                   == Regex.Count(everything, $@"Raise\(\s*new\s+{Regex.Escape(name)}\s*\(")),
            ];

            if (onlyRaised.Length == 0)
            {
                continue;
            }

            bool stages = module.Any(file =>
                file.Source.Contains("EnqueueRaisedEventsAsync(", StringComparison.Ordinal)
                && !file.Path.EndsWith("/OutboxExtensions.cs", StringComparison.Ordinal));

            if (!stages)
            {
                silent.Add($"{module.Key}: {string.Join(", ", onlyRaised)}");
            }
        }

        Assert.True(
            silent.Count == 0,
            "These modules raise integration events in their aggregates and never stage them, so "
            + "the events are discarded. Call outbox.EnqueueRaisedEventsAsync(aggregate) in the "
            + "handler, before the unit of work saves."
            + Environment.NewLine
            + string.Join(Environment.NewLine, silent));
    }

    /// <summary>
    /// The catalogue a subscription is checked against is exactly what the
    /// source declares.
    /// <para>
    /// The catalogue reads event types by reflection from an uninitialised
    /// instance, which only works while each type is a literal. A record whose
    /// type became computed would drop out of the catalogue, and every
    /// subscription to it would be refused as unknown. This makes that a build
    /// failure instead.
    /// </para>
    /// </summary>
    [Fact]
    public void TheCatalogueMatchesTheDeclaredEvents()
    {
        string[] declared =
        [
            .. Declared([.. SourceFiles()]).Select(e => e.Type).Distinct().Order(StringComparer.Ordinal),
        ];

        var catalogue = new CCP.Kernel.Infrastructure.Outbox.ReflectedEventTypeCatalogue(
            typeof(Program).Assembly);

        Assert.Equal(declared, catalogue.EventTypes);
    }

    /// <summary>
    /// Proves the scan still recognises the Platform's events.
    /// <para>
    /// There were thirty-five when this was written. A pattern that stops
    /// matching reports every assertion above as passing.
    /// </para>
    /// </summary>
    [Fact]
    public void TheScanFindsTheEvents()
    {
        int declared = Declared([.. SourceFiles()]).Count();

        Assert.True(declared >= 30, $"Only {declared} integration events were recognised.");
    }

    private static IEnumerable<(string Name, string Type)> Declared(
        IEnumerable<(string Path, string Source)> sources)
        => sources
            .SelectMany(file => Declaration().Matches(file.Source))
            .Select(match => (match.Groups["name"].Value, match.Groups["type"].Value))
            .Distinct();

    private static string ModuleOf(string path)
    {
        const string marker = "/src/Modules/";
        int start = path.IndexOf(marker, StringComparison.Ordinal) + marker.Length;

        return path[start..path.IndexOf('/', start)];
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
