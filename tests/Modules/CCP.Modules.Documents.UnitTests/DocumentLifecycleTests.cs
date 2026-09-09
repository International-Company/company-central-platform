using CCP.Kernel.Results;
using CCP.Modules.Documents.Domain.Documents;

namespace CCP.Modules.Documents.UnitTests;

/// <summary>
/// Versioning, and the two stages of deletion.
/// </summary>
public sealed class DocumentLifecycleTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);

    private static readonly Guid Owner = Guid.CreateVersion7();

    private static Document ADocument()
    {
        Result<Document> created = Document.Create("Employment contract", "hr", Owner, null, Now);

        Assert.True(created.IsSuccess);

        return created.Value;
    }

    private static Result<DocumentVersion> AVersion(Document document, DateTimeOffset at) =>
        document.AddVersion(
            "contract.pdf", "application/pdf", 1024, new string('a', 64),
            Guid.NewGuid().ToString("N"), Owner, at);

    [Fact]
    public void VersionsStartAtOneAndCountUp()
    {
        Document document = ADocument();

        Assert.Equal(1, AVersion(document, Now).Value.VersionNumber);
        Assert.Equal(2, AVersion(document, Now.AddDays(1)).Value.VersionNumber);
        Assert.Equal(2, document.CurrentVersionNumber);
    }

    [Fact]
    public void AnOlderVersionStaysWhereItWas()
    {
        Document document = ADocument();

        DocumentVersion first = AVersion(document, Now).Value;
        DocumentVersion second = AVersion(document, Now.AddDays(1)).Value;

        // The reason versioning exists: somebody signed the first one, and
        // replacing it in place would leave a signature attached to text nobody
        // has read.
        Assert.NotEqual(first.ObjectKey, second.ObjectKey);
        Assert.Equal(first.ObjectKey, document.VersionNumbered(1)!.ObjectKey);
        Assert.Equal(2, document.CurrentVersion!.VersionNumber);
    }

    [Fact]
    public void DeletionDoesNotTouchTheContent()
    {
        Document document = ADocument();
        AVersion(document, Now);

        Result marked = document.MarkForDeletion(Owner, TimeSpan.FromDays(30), Now);

        Assert.True(marked.IsSuccess);
        Assert.Equal(DocumentStatus.MarkedForDeletion, document.Status);
        Assert.Equal(Now.AddDays(30), document.PurgeAfter);
        Assert.All(document.Versions, v => Assert.False(v.ContentRemoved));
    }

    [Fact]
    public void PurgingBeforeTheGracePeriodIsRefused()
    {
        Document document = ADocument();
        AVersion(document, Now);
        document.MarkForDeletion(Owner, TimeSpan.FromDays(30), Now);

        Result purged = document.Purge(Now.AddDays(29));

        Assert.True(purged.IsFailure);
        Assert.Equal("DOCUMENTS.GRACE_PERIOD_NOT_ELAPSED", purged.Error.Code);
        Assert.Equal(DocumentStatus.MarkedForDeletion, document.Status);
    }

    [Fact]
    public void PurgingKeepsEverythingSaidAboutTheContentItDestroys()
    {
        Document document = ADocument();
        DocumentVersion version = AVersion(document, Now).Value;
        document.MarkForDeletion(Owner, TimeSpan.FromDays(30), Now);

        Result purged = document.Purge(Now.AddDays(31));

        Assert.True(purged.IsSuccess);
        Assert.Equal(DocumentStatus.Purged, document.Status);
        Assert.True(version.ContentRemoved);

        // "A 1 KB PDF named contract.pdf, with this hash, destroyed on this
        // date" is an answer. An absent row is not.
        Assert.Equal("contract.pdf", version.FileName);
        Assert.Equal(1024, version.SizeInBytes);
        Assert.NotEmpty(version.Sha256);
    }

    [Fact]
    public void APurgedDocumentRefusesEverything()
    {
        Document document = ADocument();
        AVersion(document, Now);
        document.MarkForDeletion(Owner, TimeSpan.FromDays(30), Now);
        document.Purge(Now.AddDays(31));

        Assert.Equal("DOCUMENTS.PURGED", document.Rename("Anything", null, Now).Error.Code);
        Assert.Equal("DOCUMENTS.PURGED", document.Restore(Now).Error.Code);
        Assert.Equal("DOCUMENTS.PURGED", AVersion(document, Now).Error.Code);
    }

    [Fact]
    public void RestoringInsideTheGracePeriodClearsTheSchedule()
    {
        Document document = ADocument();
        document.MarkForDeletion(Owner, TimeSpan.FromDays(30), Now);

        Result restored = document.Restore(Now.AddDays(5));

        Assert.True(restored.IsSuccess);
        Assert.Equal(DocumentStatus.Active, document.Status);
        Assert.Null(document.PurgeAfter);
        Assert.Null(document.MarkedForDeletionBy);
    }

    [Fact]
    public void UploadingIntoADeletedDocumentBringsItBack()
    {
        Document document = ADocument();
        document.MarkForDeletion(Owner, TimeSpan.FromDays(30), Now);

        AVersion(document, Now.AddDays(2));

        // Somebody adding a version to a document scheduled for destruction has
        // said clearly enough that they want it. Leaving it scheduled would
        // destroy the version they just added.
        Assert.Equal(DocumentStatus.Active, document.Status);
        Assert.Null(document.PurgeAfter);
    }

    [Fact]
    public void RestoringSomethingThatWasNotDeletedIsRefused()
    {
        Document document = ADocument();

        Assert.Equal("DOCUMENTS.NOT_MARKED_FOR_DELETION", document.Restore(Now).Error.Code);
    }

    [Fact]
    public void ADocumentNeedsATitleAndAnOwner()
    {
        Assert.True(Document.Create("  ", null, Owner, null, Now).IsFailure);
        Assert.True(Document.Create("Title", null, Guid.Empty, null, Now).IsFailure);
    }
}
