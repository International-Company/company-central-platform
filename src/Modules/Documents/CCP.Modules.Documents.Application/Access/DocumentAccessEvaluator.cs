using CCP.Modules.Documents.Application.Abstractions;
using CCP.Modules.Documents.Domain.Access;
using CCP.Modules.Documents.Domain.Documents;

namespace CCP.Modules.Documents.Application.Access;

/// <summary>
/// Decides how much of a document one person may have.
/// <para>
/// <b>One function, called by everything.</b> Upload, download, sharing,
/// deletion and search all reach the same answer through here, because the way
/// access checks go wrong is not that somebody writes a bad one — it is that
/// somebody writes a second one, slightly different, and the two disagree.
/// </para>
/// <para>
/// It is a pure function of a document, its rules and a caller. No database, no
/// clock, no HTTP: everything it needs was gathered before it was called, so
/// the decision can be reasoned about, and tested, without a running system.
/// </para>
/// </summary>
public static class DocumentAccessEvaluator
{
    /// <summary>
    /// The strongest level the caller holds, or null for none at all.
    /// <para>
    /// Rules add up rather than override: somebody who is in the finance
    /// department (read) and also named individually (manage) manages it. The
    /// alternative — most specific rule wins — reads well and produces the
    /// situation where adding a broad rule silently takes access away from
    /// somebody, which nobody ever intends and nobody notices.
    /// </para>
    /// </summary>
    public static DocumentAccessLevel? Evaluate(
        Document document,
        IReadOnlyList<DocumentAccessRule> rules,
        AccessSubject caller,
        bool callerIsUnrestricted)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(caller);

        // A permission whose scope covers the whole company already means
        // "every document", and having to also be named on each one would make
        // that permission unusable — an administrator restoring a document
        // after somebody has left cannot ask them to share it first.
        if (callerIsUnrestricted)
        {
            return DocumentAccessLevel.Manage;
        }

        // The owner is not a rule. If they were, revoking rules could leave a
        // document with nobody responsible for it, and the person who uploaded
        // it locked out of what they uploaded.
        if (document.OwnerUserId == caller.UserId)
        {
            return DocumentAccessLevel.Manage;
        }

        DocumentAccessLevel? best = null;

        foreach (DocumentAccessRule rule in rules)
        {
            if (!Matches(rule, caller))
            {
                continue;
            }

            if (best is null || rule.Level > best)
            {
                best = rule.Level;
            }
        }

        return best;
    }

    /// <summary>Whether the caller may do something needing this much.</summary>
    public static bool Allows(
        Document document,
        IReadOnlyList<DocumentAccessRule> rules,
        AccessSubject caller,
        bool callerIsUnrestricted,
        DocumentAccessLevel required)
    {
        DocumentAccessLevel? held = Evaluate(document, rules, caller, callerIsUnrestricted);

        return held is { } level && level >= required;
    }

    private static bool Matches(DocumentAccessRule rule, AccessSubject caller) =>
        rule.SubjectKind switch
        {
            AccessSubjectKind.User =>
                rule.SubjectId == caller.UserId,

            AccessSubjectKind.Role =>
                caller.RoleIds.Contains(rule.SubjectId),

            // Exactly this unit, or anywhere beneath it when the rule says so.
            // The chain is the caller's ancestry, so containment in it is the
            // whole of "is this rule above me?" — no path parsing at check time
            // and no second query.
            AccessSubjectKind.OrganizationUnit =>
                rule.IncludesSubUnits
                    ? caller.UnitChainIds.Contains(rule.SubjectId)
                    : caller.UnitId == rule.SubjectId,

            _ => false
        };
}
