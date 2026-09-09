# Documents — keeping files, and deciding who sees them

Reference: [ARCHITECTURE.md §18](../../ARCHITECTURE.md) · [ADR-014](../architecture/adr/ADR-014-document-storage.md).

**Metadata in PostgreSQL, bytes in object storage.** The row is not the file.

---

## 1. Storing something

```http
POST /api/v1/documents
Authorization: Bearer <token with platform.documents.create>
Content-Type: multipart/form-data

file:                 (the bytes)
title:                Signed supply agreement
category:             contract
organizationUnitId:   (optional)
```

The uploader becomes the owner, and the owner manages the document. That is the
only sane default: at the moment of upload nobody else can have been given
access.

### What happens to the bytes, in order

1. **Buffered** to a temporary file, counting as it goes. The size limit stops
   the copy rather than checking afterwards — checking a length after the fact
   means having already written a two-gigabyte file to disk to discover it was
   not allowed.
2. **Identified** by reading the first bytes. The declared type and the
   extension are both attacker-controlled, and both are trivially set to
   `application/pdf` on a Windows executable.
3. **Hashed** — SHA-256, so "is this the file we were given?" still has an
   answer after the storage has been migrated twice.
4. **Scanned**, if a scanner is configured.
5. **Written** to object storage under a random key.

A refusal at any step leaves nothing behind: no object, no row, no
half-inspected file waiting in a bucket for somebody to notice it.

### What is accepted

PDF, PNG, JPEG, GIF, WebP, plain text, and the ZIP-based office formats
(`.docx`, `.xlsx`, `.pptx`, `.odt`, `.ods`) plus plain `.zip`.

Programs are refused **and named** — "a Windows program", "a script" — because
the common cause is a file renamed to get past an extension check and the second
most common is an honest mistake, and both are cleared up by being told what the
file really is.

Note what accepting a ZIP does not promise. A ZIP is a container, and taking one
means taking whatever is inside it, including an Office document carrying a
macro. That is what the scanner hook is for; refusing every ZIP-based format
instead would mean refusing Word and Excel, which no company can live with.

### The object key says nothing

Twenty random bytes, sharded on the first two characters. Not the file name —
which invites path traversal and collisions — and not the document id, which
would make keys guessable the day a bucket is misconfigured.

The original file name is stored as metadata and used for one thing: telling the
browser what to save the file as.

---

## 2. Getting it back

```http
GET /api/v1/documents/{id}/content        # the current version
GET /api/v1/documents/{id}/content?version=1
```

Two shapes, one endpoint. When the store can issue a short-lived pre-signed URL
the caller is redirected to it and the bytes never pass through the Platform;
when it cannot, the Platform streams them. A client follows whichever it gets,
so the deployment can change underneath it.

**The pre-signed URL is a bearer credential** for its lifetime — five minutes by
default. Anybody holding it can fetch the file, and it will end up in a browser
history and a proxy log. Short enough not to be worth passing on; long enough to
start a download on a slow connection.

The bucket is **never publicly readable**. If it were, every access rule in this
module would be decoration.

---

## 3. Who may see what

Two checks, and both are needed.

| Check | Answers | Where |
|---|---|---|
| Permission | May this person work with documents at all? | The endpoint |
| Access rule | Which documents? | `DocumentAccessEvaluator` |

Holding `platform.documents.read` and nothing else lets somebody read what they
own and what has been shared with them. A permission alone would mean every file
in the company.

### Rules

A rule names a **user**, a **role**, or an **organizational unit** — and for a
unit, whether it reaches the units beneath it. Both readings of that are
legitimate: a policy shared with a whole division wants the subtree; a document
shared with the finance department specifically does not. So it is asked rather
than assumed.

Three levels, and comparison is the whole check:

| Level | Allows |
|---|---|
| Read | See it, download it |
| Write | Read, and add a version |
| Manage | Write, and share it, move it, delete it, read its access history |

**Rules add up; they do not override.** Somebody in the finance department
(read) who is also named individually (manage) manages it. The alternative —
most specific wins — reads well and produces the situation where adding a broad
rule silently takes access away from somebody, which nobody intends and nobody
notices.

The **owner is not a rule**. If they were, revoking rules could leave a document
with nobody responsible for it and its uploader locked out of what they uploaded.

A caller whose permission is granted company-wide reaches everything. An
administrator restoring a document after somebody has left cannot ask them to
share it first.

---

## 4. Versions

A new upload adds a row; it never edits one. The previous version keeps its
object key and stays downloadable.

That is the whole point: somebody signed *that* copy, and replacing it in place
would leave the company with a signature attached to text nobody has seen.

---

## 5. Attaching a document to a business record

```http
POST /api/v1/documents/{id}/links
{ "resourceType": "purchase-order", "resourceId": "PO-2026-0041" }

GET /api/v1/resources/purchase-order/PO-2026-0041/documents
```

Two strings, meaning nothing to the Platform. There is no foreign key and there
cannot be one: the Platform is built before the systems that use it and must
outlive any of them, and a constraint pointing at `purchasing.orders` would make
this module undeployable without the purchasing system and undeletable with it.

The consequence is honest and worth stating: **a link can outlive the record it
points at.** That shows as a link the caller cannot resolve, which is a great
deal better than a module that refuses to start.

The listing is filtered by what the caller may see, so two people opening the
same order can correctly see different numbers of documents.

### The upload control is reusable

`DocumentUpload` takes `resourceType` and `resourceId` and forwards them. A
purchase order screen renders it, gets a document back, and never learns anything
about storage, versions or access rules.

---

## 6. Deletion happens twice

**Mark**, then **purge**.

Marking removes the document from listings and touches nothing else. Inside the
grace period — thirty days by default — anybody who manages it can restore it.
Uploading a new version restores it too: somebody adding to a document scheduled
for destruction has said clearly enough that they want it.

Purging destroys the content. **The record survives**: the metadata row, every
version and the whole access history stay, because "this document existed, these
people read it, and it was destroyed on this date by this person" is the question
asked *after* a deletion, and a row that was removed cannot answer it. A purged
version still knows it was a 2.3 MB PDF called `contract-final.pdf` with a
particular hash.

The sweep deletes the bytes **before** it marks the record. The other order can
leave a document marked destroyed whose content is still in the bucket — a false
statement in the one place a company will be asked to prove something. This
order can leave content whose row still says it exists, and the next sweep
removes it again; removal is idempotent precisely so retrying is safe.

Two stages exist because otherwise a misclick and a legal instruction look
identical to the system, and only one of them should be able to destroy
something.

---

## 7. The access log

**Every access is recorded, including the ones that were refused.**

The refusals are the more interesting half. One person failing to open a
document is a wrong link; one person failing to open forty is something else,
and a log of successes shows neither.

Reading it needs `Manage`. It is a list of names against times, and a document
shared with forty people would otherwise tell each of them what the other
thirty-nine had been reading.

It is separate from the Platform audit trail on purpose. The audit trail records
changes; this records **reads**, which are not changes and which for documents
are exactly what somebody will need to reconstruct later.

---

## 8. Configuration

```jsonc
"Documents": {
  "MaxFileSizeInBytes": 26214400,      // 25 MB
  "DeletionGracePeriod": "30.00:00:00",
  "PurgeSweepInterval": "06:00:00",
  "PurgeBatchSize": 50,
  "DownloadUrlLifetime": "00:05:00",
  "RejectWhenScannerUnavailable": true
}
```

`RejectWhenScannerUnavailable` is the one setting with a real argument on both
sides. Blocking means a scanner outage stops people working; not blocking means
an outage is precisely when unscanned files enter the system, which is what
somebody would arrange if they could. The Platform takes the safe side and lets
a company decide otherwise deliberately.

### Storage

Object storage is used when it is configured, and a directory on disk when it is
not — **not** decided by the environment name. "Production means S3" works until
the first production deployment without a bucket, which then fails at the first
upload rather than at startup.

```
CCP_Documents__S3__BucketName=ccp-documents
CCP_Documents__S3__ServiceUrl=https://…        # anything S3-compatible
CCP_Documents__S3__Region=eu-west-1
CCP_Documents__S3__AccessKeyId=…
CCP_Documents__S3__SecretAccessKey=…
```

**Through the environment only.** No credential has a default and none appears
in any file in this repository (§21.1). Locally, `docker compose up minio` gives
an S3-compatible endpoint on `http://localhost:9000`.

### Scanning

None ships. Bundling one would mean choosing a vendor, a licence and a
deployment shape for every company that installs this.

What the default does **not** do is lie: it reports `NotScanned` rather than
`Clean`, and that verdict is written into the upload's log entry — so an audit of
what was checked tells the truth. Replacing it is one line:

```csharp
services.AddScoped<IDocumentScanner, ClamAvScanner>();
```

---

## 9. Permissions

| Permission | For |
|---|---|
| `platform.documents.read` | Searching, viewing, downloading |
| `platform.documents.create` | Uploading |
| `platform.documents.update` | New versions, renaming, linking |
| `platform.documents.share` | Access rules |
| `platform.documents.delete` | Marking for deletion, restoring |

Each is necessary and none is sufficient. The access rules decide the rest.
