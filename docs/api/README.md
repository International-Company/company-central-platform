# API Documentation

Reference: [ARCHITECTURE.md §11](../../ARCHITECTURE.md) · [ADR-008](../architecture/adr/ADR-008-api-strategy.md).

## Planned documents

| Document | Phase |
|---|---|
| `conventions.md` — paths, methods, status codes, headers | Phase 1 |
| `errors.md` — the Problem Details shape and the full error code catalogue | Phase 1 |
| `pagination-filtering-sorting.md` | Phase 1 |
| ✅ [`versioning.md`](versioning.md) — what may change inside a version, and the deprecation policy | Phase 11 |
| `authentication.md` — obtaining and using a token | Covered by [the integration guide](../development/integration-guide.md) §2–3 |
| `openapi.md` — where the spec lives and how to generate a client | See [`versioning.md`](versioning.md) §5 |

## At a glance

```
Base path      /api/v1
Errors         RFC 9457 Problem Details + code + correlationId
Pagination     ?page=1&pageSize=25   (cursor also offered on large collections)
Sorting        ?sort=-createdAt      (allow-listed, indexed columns only)
Correlation    X-Correlation-Id      (accepted and echoed; generated when absent)
Time           ISO 8601, UTC
```

Every error response carries a `correlationId`. When reporting a problem, quote it — it retrieves the log, the trace and the audit record for that exact request.

