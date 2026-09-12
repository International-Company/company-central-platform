using CCP.Kernel.Results;

namespace CCP.Modules.Documents.Domain;

/// <summary>
/// Every way storing, reading or removing a document can be refused, said in
/// words a person can act on.
/// </summary>
public static class DocumentErrors
{
    // --- Metadata -----------------------------------------------------------

    public static readonly Error TitleRequired = Error.Validation(
        "DOCUMENTS.TITLE_REQUIRED", "A document needs a title.", "title");

    public static readonly Error FileNameRequired = Error.Validation(
        "DOCUMENTS.FILE_NAME_REQUIRED", "The file has no name.", "fileName");

    public static readonly Error OwnerRequired = Error.Validation(
        "DOCUMENTS.OWNER_REQUIRED", "A document needs an owner.", "ownerUserId");

    public static readonly Error NotFound = Error.NotFound(
        "DOCUMENTS.NOT_FOUND", "The document does not exist.");

    public static readonly Error VersionNotFound = Error.NotFound(
        "DOCUMENTS.VERSION_NOT_FOUND", "That version of the document does not exist.");

    // --- Content ------------------------------------------------------------

    public static readonly Error EmptyFile = Error.Validation(
        "DOCUMENTS.EMPTY_FILE", "The file is empty.", "file");

    public static Error FileTooLarge(long sizeInBytes, long limitInBytes) => Error.Validation(
        "DOCUMENTS.FILE_TOO_LARGE",
        $"The file is {sizeInBytes / 1024 / 1024} MB. The limit is {limitInBytes / 1024 / 1024} MB.",
        "file");

    /// <summary>
    /// The bytes are not one of the types this Platform accepts.
    /// <para>
    /// The message names what was actually found, not what was claimed, because
    /// the common cause is a file renamed to get past an extension check and the
    /// second most common is a genuine mistake — and both are cleared up by
    /// being told what the file really is.
    /// </para>
    /// </summary>
    public static Error FileTypeNotAllowed(string detected) => Error.Validation(
        "DOCUMENTS.FILE_TYPE_NOT_ALLOWED",
        $"Files of type '{detected}' are not accepted.",
        "file");

    /// <summary>
    /// The bytes are a type we accept; the caller said they were another.
    /// <para>
    /// Refused rather than silently corrected. A mismatch is either an attack or
    /// a bug in the caller, and storing the file under the type we detected
    /// would hide both.
    /// </para>
    /// </summary>
    public static Error DeclaredTypeMismatch(string declared, string detected) => Error.Validation(
        "DOCUMENTS.DECLARED_TYPE_MISMATCH",
        $"The file was declared as '{declared}' and its content is '{detected}'.",
        "contentType");

    public static Error ScanRejected(string detail) => Error.Validation(
        "DOCUMENTS.SCAN_REJECTED",
        $"The file was rejected by the malware scan: {detail}",
        "file");

    public static readonly Error ContentUnavailable = Error.Conflict(
        "DOCUMENTS.CONTENT_UNAVAILABLE",
        "The stored content of this version is no longer available.");

    // --- Lifecycle ----------------------------------------------------------

    public static readonly Error AlreadyMarkedForDeletion = Error.Conflict(
        "DOCUMENTS.ALREADY_MARKED_FOR_DELETION",
        "The document is already marked for deletion.");

    public static readonly Error NotMarkedForDeletion = Error.Conflict(
        "DOCUMENTS.NOT_MARKED_FOR_DELETION",
        "The document is not marked for deletion, so there is nothing to restore.");

    /// <summary>
    /// Something was attempted on a document whose bytes are gone.
    /// <para>
    /// Purged is not deleted: the metadata row and its access history stay, so
    /// "what was here and who read it" survives the content. Every operation on
    /// one fails here rather than half-succeeding.
    /// </para>
    /// </summary>
    public static readonly Error Purged = Error.Conflict(
        "DOCUMENTS.PURGED",
        "The document has been purged. Its record remains; its content does not.");

    public static readonly Error GracePeriodNotElapsed = Error.Conflict(
        "DOCUMENTS.GRACE_PERIOD_NOT_ELAPSED",
        "The document cannot be purged until its grace period has elapsed.");

    // --- Access -------------------------------------------------------------

    /// <summary>
    /// The caller holds the permission and not the reach.
    /// <para>
    /// Deliberately identical in wording to <see cref="NotFound"/> would be the
    /// safer choice for a public system; here it is not, because an employee
    /// being told "you may not read this" is actionable and being told "it does
    /// not exist" sends them to ask why the reference they were given is broken.
    /// The existence of a document is not the secret; its content is.
    /// </para>
    /// </summary>
    public static readonly Error AccessDenied = Error.Forbidden(
        "DOCUMENTS.ACCESS_DENIED", "You do not have access to this document.");

    public static readonly Error AccessRuleNotFound = Error.NotFound(
        "DOCUMENTS.ACCESS_RULE_NOT_FOUND", "The access rule does not exist.");

    public static readonly Error SubjectRequired = Error.Validation(
        "DOCUMENTS.SUBJECT_REQUIRED", "An access rule needs a subject.", "subjectId");

    // --- Linking ------------------------------------------------------------

    public static readonly Error ResourceTypeRequired = Error.Validation(
        "DOCUMENTS.RESOURCE_TYPE_REQUIRED", "A link needs a resource type.", "resourceType");

    public static readonly Error ResourceIdRequired = Error.Validation(
        "DOCUMENTS.RESOURCE_ID_REQUIRED", "A link needs a resource id.", "resourceId");

    public static readonly Error LinkExists = Error.Conflict(
        "DOCUMENTS.LINK_EXISTS", "The document is already linked to that record.");

    public static readonly Error LinkNotFound = Error.NotFound(
        "DOCUMENTS.LINK_NOT_FOUND", "The link does not exist.");
}
