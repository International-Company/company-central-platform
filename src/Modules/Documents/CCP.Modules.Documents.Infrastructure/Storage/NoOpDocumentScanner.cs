using CCP.Modules.Documents.Application.Abstractions;

namespace CCP.Modules.Documents.Infrastructure.Storage;

/// <summary>
/// The scanner that does not scan, and says so.
/// <para>
/// The Platform ships without a malware scanner because bundling one would mean
/// choosing a vendor, a licence and a deployment shape on behalf of every
/// company that installs this — and a scanner nobody chose is a scanner nobody
/// maintains.
/// </para>
/// <para>
/// <b>What it does not do is lie.</b> It returns
/// <see cref="ScanVerdict.NotScanned"/> rather than <c>Clean</c>, that verdict
/// is written into the access log entry for the upload, and so a version record
/// says plainly whether anything ever looked at the file. A default that
/// reported "clean" would produce an audit trail asserting that every file the
/// company holds was checked, which is the kind of false record that is worse
/// than no record.
/// </para>
/// <para>
/// Replacing it is one registration:
/// <c>services.AddScoped&lt;IDocumentScanner, ClamAvScanner&gt;();</c>
/// </para>
/// </summary>
public sealed class NoOpDocumentScanner : IDocumentScanner
{
    public string Name => "none";

    public Task<ScanResult> ScanAsync(
        Stream content, CancellationToken cancellationToken = default)
        => Task.FromResult(ScanResult.NotScanned);
}
