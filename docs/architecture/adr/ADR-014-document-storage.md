# ADR-014: Document Storage — Object Storage, Not the Database

| Field | Value |
|---|---|
| Status | **Accepted** |
| Date | 2026-09-06 |
| Deciders | Platform Architecture |

## Context

The Documents module stores files uploaded by users and by business systems, with metadata, versioning and access control. Volume and file sizes are unknown but will grow continuously, and documents are rarely deleted.

## Problem

Where does binary content live, and how is access controlled?

## Options

### Option A — In PostgreSQL (`bytea` or large objects)
*Pros:* one store; transactional with metadata; one backup.
*Cons:* database size grows without bound; backups become slow and large, which directly damages the RTO; large transfers occupy connections from a pool sized for short queries; restore time becomes unacceptable. A database is a poor filesystem.

### Option B — Object storage (S3-compatible)
*Pros:* built for this; cheap; effectively unbounded; versioning and lifecycle policies built in; content transfer bypasses the database entirely; pre-signed URLs allow direct download without proxying bytes through the application.
*Cons:* no transaction spanning storage and metadata, so orphan objects are possible and need reconciliation; a second store to secure and back up.

### Option C — A server filesystem
*Cons:* contradicts the cloud-first requirement; not shared across instances; needs its own backup; does not survive a container restart.

## Decision

**Option B.** Metadata in PostgreSQL, content in S3-compatible object storage, behind `IDocumentStorageProvider` — a local filesystem implementation for development, S3-compatible in production.

Security decisions, all binding:
- Content type is determined by **inspecting magic bytes**, never by trusting the client's declared type or file extension.
- An allow-list of permitted types and a configurable size limit.
- Objects are keyed by a **random key, never by the original filename** (which is stored as metadata only) — filenames as keys invite path traversal and collisions.
- SHA-256 computed for integrity and deduplication.
- **Object storage is never publicly readable under any path.** Downloads are served either by a short-lived pre-signed URL issued *after* the permission check, or by a streaming endpoint that checks permission and then proxies.
- Every download is logged with actor, time and IP.
- A scanning hook (`IDocumentScanner`) runs before a document becomes available, with a no-op default and a documented production option.
- Deletion is two-stage — mark, then purge after a grace period — so an accidental deletion is recoverable.

## Reason

The decisive argument against Option A is recovery time. Documents in the database inflate every backup and every restore, and the RTO in ARCHITECTURE.md §23 is measured in hours. Keeping binaries out of the database keeps the database small, its backups fast, and its restore predictable.

The security rules exist because file upload is one of the most commonly exploited features in any application. Trusting a declared content type, or using a client-supplied filename as a storage key, are the two classic mistakes; both are ruled out explicitly rather than left to the implementer's judgement.

## Consequences

**Positive.** The database stays small and restores quickly. Storage scales without planning. Versioning and lifecycle policies come from the storage service. Large transfers do not consume database connections. Pre-signed URLs remove file bytes from the application's bandwidth entirely.

**Negative.** No transaction across the two stores, so an upload that writes an object and then fails to write metadata leaves an orphan — a reconciliation job is required. Two stores to secure, back up and monitor. Pre-signed URLs are bearer credentials for their lifetime, so expiry must be short, access must be logged, and the most sensitive documents should use the streaming endpoint instead.

**Follow-up actions.**
- MinIO in Docker Compose for local development (Phase 1); the real service from Phase 19.
- An orphan-object reconciliation job (Phase 10).
- Object storage versioning and cross-region replication configured as part of the backup strategy (Phases 17 and 19).
- Explicit security tests: mismatched magic bytes rejected, direct object URL not readable, pre-signed URL expires, path traversal via filename impossible (Phase 10).

## Status
Accepted.

