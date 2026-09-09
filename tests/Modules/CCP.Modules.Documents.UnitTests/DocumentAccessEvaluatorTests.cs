using CCP.Modules.Documents.Application.Abstractions;
using CCP.Modules.Documents.Application.Access;
using CCP.Modules.Documents.Domain.Access;
using CCP.Modules.Documents.Domain.Documents;

namespace CCP.Modules.Documents.UnitTests;

/// <summary>
/// Who may have which document.
/// <para>
/// The evaluator is a pure function, which is why these tests need no database
/// and no host — and why the decision it makes can be reasoned about at all.
/// </para>
/// </summary>
public sealed class DocumentAccessEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);

    private static readonly Guid Owner = Guid.CreateVersion7();
    private static readonly Guid Stranger = Guid.CreateVersion7();
    private static readonly Guid Role = Guid.CreateVersion7();
    private static readonly Guid Division = Guid.CreateVersion7();
    private static readonly Guid Team = Guid.CreateVersion7();

    private static Document ADocument() =>
        Document.Create("Policy", null, Owner, Division, Now).Value;

    private static DocumentAccessRule ARule(
        AccessSubjectKind kind, Guid subject, DocumentAccessLevel level, bool subUnits = false) =>
        DocumentAccessRule.Create(
            Guid.CreateVersion7(), kind, subject, level, subUnits, Now).Value;

    [Fact]
    public void TheOwnerManagesTheirOwnDocumentWithNoRule()
    {
        DocumentAccessLevel? level = DocumentAccessEvaluator.Evaluate(
            ADocument(), [], AccessSubject.Bare(Owner), callerIsUnrestricted: false);

        // Not expressed as a rule, so that revoking rules can never leave a
        // document nobody is responsible for and its uploader locked out.
        Assert.Equal(DocumentAccessLevel.Manage, level);
    }

    [Fact]
    public void SomebodyWithNoRuleGetsNothing()
    {
        DocumentAccessLevel? level = DocumentAccessEvaluator.Evaluate(
            ADocument(), [], AccessSubject.Bare(Stranger), callerIsUnrestricted: false);

        Assert.Null(level);
    }

    [Fact]
    public void ACompanyWideGrantReachesEverything()
    {
        DocumentAccessLevel? level = DocumentAccessEvaluator.Evaluate(
            ADocument(), [], AccessSubject.Bare(Stranger), callerIsUnrestricted: true);

        Assert.Equal(DocumentAccessLevel.Manage, level);
    }

    [Fact]
    public void ARoleRuleReachesWhoeverHoldsTheRole()
    {
        var caller = new AccessSubject(Stranger, [Role], null, []);

        DocumentAccessLevel? level = DocumentAccessEvaluator.Evaluate(
            ADocument(), [ARule(AccessSubjectKind.Role, Role, DocumentAccessLevel.Write)],
            caller, callerIsUnrestricted: false);

        Assert.Equal(DocumentAccessLevel.Write, level);
    }

    [Fact]
    public void AUnitRuleWithoutSubUnitsStopsAtThatUnit()
    {
        // Somebody in a team three levels down, and a rule on the division that
        // was deliberately not marked as reaching beneath it.
        var caller = new AccessSubject(Stranger, [], Team, [Division, Team]);

        DocumentAccessLevel? level = DocumentAccessEvaluator.Evaluate(
            ADocument(),
            [ARule(AccessSubjectKind.OrganizationUnit, Division, DocumentAccessLevel.Read)],
            caller,
            callerIsUnrestricted: false);

        Assert.Null(level);
    }

    [Fact]
    public void AUnitRuleWithSubUnitsReachesDownTheTree()
    {
        var caller = new AccessSubject(Stranger, [], Team, [Division, Team]);

        DocumentAccessLevel? level = DocumentAccessEvaluator.Evaluate(
            ADocument(),
            [ARule(AccessSubjectKind.OrganizationUnit, Division, DocumentAccessLevel.Read, subUnits: true)],
            caller,
            callerIsUnrestricted: false);

        Assert.Equal(DocumentAccessLevel.Read, level);
    }

    [Fact]
    public void RulesAddUpRatherThanOverride()
    {
        var caller = new AccessSubject(Stranger, [Role], Team, [Division, Team]);

        DocumentAccessLevel? level = DocumentAccessEvaluator.Evaluate(
            ADocument(),
            [
                ARule(AccessSubjectKind.OrganizationUnit, Division, DocumentAccessLevel.Read, subUnits: true),
                ARule(AccessSubjectKind.User, Stranger, DocumentAccessLevel.Manage),
                ARule(AccessSubjectKind.Role, Role, DocumentAccessLevel.Write)
            ],
            caller,
            callerIsUnrestricted: false);

        // Most-specific-wins reads well and produces the situation where adding
        // a broad rule silently takes access away from somebody.
        Assert.Equal(DocumentAccessLevel.Manage, level);
    }

    [Fact]
    public void ReadDoesNotStretchToWrite()
    {
        var caller = new AccessSubject(Stranger, [], null, []);
        var rules = new[] { ARule(AccessSubjectKind.User, Stranger, DocumentAccessLevel.Read) };

        Assert.True(DocumentAccessEvaluator.Allows(
            ADocument(), rules, caller, false, DocumentAccessLevel.Read));

        Assert.False(DocumentAccessEvaluator.Allows(
            ADocument(), rules, caller, false, DocumentAccessLevel.Write));

        Assert.False(DocumentAccessEvaluator.Allows(
            ADocument(), rules, caller, false, DocumentAccessLevel.Manage));
    }

    [Fact]
    public void SubUnitsIsIgnoredOnAnythingThatIsNotAUnit()
    {
        // Asking for it on a user rule is meaningless, and storing it would
        // leave a field whose value depends on which kind of rule it is on.
        DocumentAccessRule rule = ARule(
            AccessSubjectKind.User, Stranger, DocumentAccessLevel.Read, subUnits: true);

        Assert.False(rule.IncludesSubUnits);
    }

    [Fact]
    public void ARuleNeedsASubject()
    {
        Assert.True(DocumentAccessRule.Create(
            Guid.CreateVersion7(), AccessSubjectKind.User, Guid.Empty,
            DocumentAccessLevel.Read, false, Now).IsFailure);
    }
}
