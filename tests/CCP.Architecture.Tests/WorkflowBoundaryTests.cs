using System.Reflection;
using System.Text.RegularExpressions;

namespace CCP.Architecture.Tests;

/// <summary>
/// The workflow engine holds no business rule.
/// <para>
/// <b>This is Phase 8's central acceptance criterion, and it is the kind that
/// erodes by increments.</b> Nobody decides to put a purchase threshold in the
/// approval engine. Somebody adds "if the amount is over ten thousand, route to
/// the director" at five o'clock because the alternative is a conversation about
/// callbacks, and a year later the module is the purchasing system's approval
/// logic wearing a general name — unusable by the next system, which is the one
/// thing it existed to be.
/// </para>
/// <para>
/// So the boundary is checked rather than reviewed. A test is what makes the
/// shortcut a decision again.
/// </para>
/// </summary>
public sealed class WorkflowBoundaryTests
{
    private static readonly string WorkflowRoot = LocateWorkflowSource();

    /// <summary>
    /// Words that describe money or a quantity to compare against.
    /// <para>
    /// Matched on identifiers, not on prose: the comments in this module discuss
    /// thresholds at length, precisely to explain why there are none, and a
    /// guard that flagged its own justification would train people to delete the
    /// explanation rather than obey the rule.
    /// </para>
    /// </summary>
    private static readonly Regex BusinessVocabulary = new(
        @"\b(amount|threshold|currency|price|salary|budget|invoice|purchase|discount|"
        + @"minimumValue|maximumValue|limitValue)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact]
    public void NoBusinessVocabularyAppearsInWorkflowCode()
    {
        var offenders = new List<string>();

        foreach (string file in SourceFiles())
        {
            string code = WithoutComments(File.ReadAllText(file));

            foreach (Match match in BusinessVocabulary.Matches(code))
            {
                offenders.Add($"{Path.GetFileName(file)}: {match.Value}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "The workflow engine must contain no business vocabulary. Conditional routing "
            + "that depends on business data belongs to the calling application, which "
            + "supplies assignees at start time or answers a callback (ARCHITECTURE.md "
            + "§16.3). Found:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders.Distinct()));
    }

    [Fact]
    public void TheEngineReferencesNoOtherModulesInternals()
    {
        // Contracts only. The assignee resolver reaches Organization and
        // Authorization through their published surfaces and nothing else; a
        // reference to another module's Domain, Application or Infrastructure
        // would make the engine unliftable, which is the property that lets a
        // future product take it unchanged.
        var offenders = new List<string>();

        foreach (string project in Directory.GetFiles(
            WorkflowRoot, "*.csproj", SearchOption.AllDirectories))
        {
            foreach (string line in File.ReadAllLines(project))
            {
                if (!line.Contains("ProjectReference", StringComparison.Ordinal))
                {
                    continue;
                }

                bool otherModule = line.Contains("CCP.Modules.", StringComparison.Ordinal)
                                && !line.Contains("CCP.Modules.Workflow.", StringComparison.Ordinal);

                if (otherModule && !line.Contains(".Contracts", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(project)}: {line.Trim()}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Workflow may reference another module's Contracts and nothing else:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void EveryActionTypeIsAccountedForInTheStateMachine()
    {
        // A verb the engine knows but the instance does not handle is a request
        // that would be accepted and then do nothing — the worst outcome
        // available, because the person believes they acted.
        Type actionType = typeof(CCP.Modules.Workflow.Domain.Definitions.WorkflowActionType);

        string instanceSource = File.ReadAllText(Path.Combine(
            WorkflowRoot, "CCP.Modules.Workflow.Domain", "Instances", "WorkflowInstance.cs"));

        var unhandled = Enum.GetNames(actionType)
            .Where(name => !instanceSource.Contains(name, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            unhandled.Count == 0,
            "These actions exist in the enum but are not mentioned by the state machine:"
            + Environment.NewLine + string.Join(Environment.NewLine, unhandled));
    }

    private static IEnumerable<string> SourceFiles()
        => Directory
            .GetFiles(WorkflowRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !path.Contains("Migrations", StringComparison.Ordinal));

    /// <summary>
    /// The file's code with comments removed.
    /// <para>
    /// The module's comments explain at length why it holds no thresholds. A
    /// guard that read prose would report that explanation as the violation.
    /// </para>
    /// </summary>
    private static string WithoutComments(string source)
        => Regex.Replace(
            Regex.Replace(source, @"/\*[\s\S]*?\*/", string.Empty),
            @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

    private static string LocateWorkflowSource()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "Modules", "Workflow");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The Workflow module source could not be located.");
    }
}
