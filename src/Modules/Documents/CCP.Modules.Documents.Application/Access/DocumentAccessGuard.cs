using CCP.Kernel.Application.Abstractions;
using CCP.Kernel.Primitives;
using CCP.Kernel.Results;
using CCP.Modules.Documents.Application.Abstractions;
using CCP.Modules.Documents.Domain;
using CCP.Modules.Documents.Domain.Access;
using CCP.Modules.Documents.Domain.Auditing;
using CCP.Modules.Documents.Domain.Documents;

namespace CCP.Modules.Documents.Application.Access;

/// <summary>
/// The door every operation goes through: load the document, decide whether the
/// caller may, and write it down either way.
/// <para>
/// <b>Logging the refusals is the reason this is one class.</b> A handler that
/// checked access itself would record the successes — that is the part somebody
/// remembers — and the interesting half of a document access log is the
/// attempts that failed. Putting the check and the record in the same place
/// makes it impossible to have one without the other.
/// </para>
/// </summary>
public sealed class DocumentAccessGuard(
    IDocumentRepository repository,
    IAccessSubjectResolver subjects,
    ICallerScope callerScope,
    ICurrentUser currentUser,
    IRequestContext requestContext,
    IDocumentUnitOfWork unitOfWork,
    IClock clock)
{
    /// <summary>A document the caller has been found entitled to.</summary>
    public sealed record AuthorizedDocument(
        Document Document,
        IReadOnlyList<DocumentAccessRule> Rules,
        AccessSubject Caller,
        DocumentAccessLevel Level);

    /// <summary>
    /// Fetches a document if the caller may have it at this level.
    /// <para>
    /// A refusal is committed on its own before the failure is returned. It has
    /// to be: the request is about to end without saving anything, and a denial
    /// recorded in a transaction nobody commits is a denial nobody can see.
    /// </para>
    /// </summary>
    public async Task<Result<AuthorizedDocument>> AuthorizeAsync(
        Guid documentId,
        DocumentAccessLevel required,
        DocumentAction action,
        int? versionNumber = null,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<AuthorizedDocument>(DocumentErrors.AccessDenied);
        }

        Document? document = await repository.GetAsync(documentId, cancellationToken);

        if (document is null)
        {
            // Nothing to log against. The access log is keyed on a document, and
            // an entry for one that does not exist would have nowhere to live
            // and nothing to tell a reader looking at a document's history.
            return Result.Failure<AuthorizedDocument>(DocumentErrors.NotFound);
        }

        IReadOnlyList<DocumentAccessRule> rules =
            await repository.GetRulesAsync(documentId, cancellationToken);

        AccessSubject caller = await subjects.ResolveAsync(userId, cancellationToken);

        DocumentAccessLevel? held = DocumentAccessEvaluator.Evaluate(
            document, rules, caller, callerScope.ReachesWholeCompany);

        if (held is not { } level || level < required)
        {
            await RecordDenialAsync(
                document.Id, versionNumber, userId, action,
                held is null
                    ? "no rule grants this caller access"
                    : $"holds {held}, needs {required}",
                cancellationToken);

            return Result.Failure<AuthorizedDocument>(DocumentErrors.AccessDenied);
        }

        return Result.Success(new AuthorizedDocument(document, rules, caller, level));
    }

    /// <summary>
    /// Writes an allowed action into the document's history.
    /// <para>
    /// Not committed here. It belongs to the same transaction as whatever it
    /// describes, so a version that was stored and an entry saying it was
    /// stored either both exist or neither does.
    /// </para>
    /// </summary>
    public void RecordSuccess(
        Guid documentId, int? versionNumber, DocumentAction action, string? detail = null)
    {
        if (currentUser.UserId is not { } userId)
        {
            return;
        }

        repository.AddAccessLog(DocumentAccessLog.Allowed(
            documentId, versionNumber, userId, action,
            requestContext.IpAddress, requestContext.UserAgent, clock.UtcNow, detail));
    }

    /// <summary>
    /// Records a refusal that the guard itself did not make — a download of
    /// content that has been purged, an upload the scanner rejected.
    /// <para>
    /// Committed immediately, for the same reason as a denial: the request is
    /// about to fail and take any uncommitted work with it.
    /// </para>
    /// </summary>
    public async Task RecordFailureAsync(
        Guid documentId,
        int? versionNumber,
        DocumentAction action,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return;
        }

        await RecordDenialAsync(documentId, versionNumber, userId, action, reason, cancellationToken);
    }

    private async Task RecordDenialAsync(
        Guid documentId,
        int? versionNumber,
        Guid userId,
        DocumentAction action,
        string reason,
        CancellationToken cancellationToken)
    {
        repository.AddAccessLog(DocumentAccessLog.Denied(
            documentId, versionNumber, userId, action, reason,
            requestContext.IpAddress, requestContext.UserAgent, clock.UtcNow));

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
