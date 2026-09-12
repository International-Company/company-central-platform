namespace CCP.Modules.Documents.Application;

/// <summary>
/// What the company has decided about storing files. Bound from configuration.
/// <para>
/// Every value here is a policy somebody should be able to change without a
/// deployment, and none of them is a secret — the storage credentials are not
/// in this class, because credentials belong in the environment and never in
/// anything that could be written to a repository (§21.1).
/// </para>
/// </summary>
public sealed class DocumentOptions
{
    public const string SectionName = "Documents";

    /// <summary>
    /// The largest file that may be uploaded. 25 MB by default.
    /// <para>
    /// A limit exists chiefly because there must be one: without it a single
    /// request can exhaust memory or disk, and "our people would not do that" is
    /// not a control. The default is chosen to comfortably hold a scanned
    /// contract and comfortably refuse a video.
    /// </para>
    /// </summary>
    public long MaxFileSizeInBytes { get; set; } = 25L * 1024 * 1024;

    /// <summary>
    /// How long a marked document survives before its content can be destroyed.
    /// Thirty days by default.
    /// <para>
    /// Long enough that somebody notices a mistake and comes back from leave;
    /// short enough that "delete it" eventually means it.
    /// </para>
    /// </summary>
    public TimeSpan DeletionGracePeriod { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How often the purge sweep looks for content it may destroy.</summary>
    public TimeSpan PurgeSweepInterval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>How many documents one sweep pass purges.</summary>
    public int PurgeBatchSize { get; set; } = 50;

    /// <summary>
    /// How often the store is compared against the version rows.
    /// <para>
    /// Daily. There is no transaction spanning a bucket and a database, so an
    /// upload that stored its object and failed before its row leaves content
    /// nothing references — which costs storage rather than correctness, and is
    /// therefore worth knowing about rather than worth interrupting anybody
    /// over.
    /// </para>
    /// </summary>
    public TimeSpan ReconciliationSweepInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// How long an object must have existed before its absence from the database
    /// means anything.
    /// <para>
    /// <b>This is what keeps the reconciliation from libelling an upload in
    /// progress.</b> Between the store and the commit, a perfectly good document
    /// is content with no row — indistinguishable from an orphan by every
    /// measure except its age. An hour is far longer than any upload and far
    /// shorter than anything anybody would call a leak.
    /// </para>
    /// </summary>
    public TimeSpan OrphanGracePeriod { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How long a pre-signed download URL stays valid. Five minutes by default.
    /// <para>
    /// Short because the URL <b>is</b> the authorization: anybody holding it can
    /// fetch the file, and it will end up in a browser history, a proxy log and
    /// occasionally a chat message. Long enough for a slow connection to start
    /// the download; not long enough for the link to be worth passing on.
    /// </para>
    /// </summary>
    public TimeSpan DownloadUrlLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether a scanner being unable to answer blocks the upload. On by default.
    /// <para>
    /// This is the one setting here with a real argument on both sides. Blocking
    /// means a scanner outage stops people working; not blocking means an
    /// outage is precisely when unscanned files enter the system, which is what
    /// somebody would arrange if they could. The Platform takes the safe side by
    /// default and lets a company decide otherwise deliberately.
    /// </para>
    /// <para>Irrelevant when no scanner is configured: nothing is claimed, and
    /// nothing is blocked.</para>
    /// </summary>
    public bool RejectWhenScannerUnavailable { get; set; } = true;
}
