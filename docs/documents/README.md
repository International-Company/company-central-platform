# Documents Documentation

Reference: [ARCHITECTURE.md §18](../../ARCHITECTURE.md) · [ADR-014](../architecture/adr/ADR-014-document-storage.md).

## Planned documents

| Document | Phase |
|---|---|
| `upload-and-download.md` | Phase 10 |
| `access-control.md` | Phase 10 |
| `versioning-and-linking.md` — attaching a document to any resource in any system | Phase 10 |
| `integration.md` — **how a business system stores and retrieves documents** | Phase 10 |
| `retention.md` | Phase 10 |

## Security rules

- Content type is determined by inspecting magic bytes — the client's declared type and file extension are never trusted.
- Objects are keyed randomly; the original filename is metadata only.
- Object storage is never publicly readable. Downloads require a permission check first.
- Every download is logged.
- Binary content never enters PostgreSQL.

