# ADR-008: API Strategy — REST, URL Versioning, RFC 9457 Errors

| Field | Value |
|---|---|
| Status | **Accepted** |
| Date | 2026-09-06 |
| Deciders | Platform Architecture |

## Context

The Platform API is a long-lived contract consumed by the administration portal and by every future business system, built by teams who may not have access to Platform source. Once a business system depends on an endpoint, breaking it is expensive. The brief specifies REST.

## Problem

How are versions expressed, errors reported, and collections queried — consistently, across every module, for years?

## Options

### Versioning: URL path (`/api/v1/...`) vs. header vs. query parameter
URL path is visible everywhere — in logs, in browser address bars, in curl commands, in support conversations. Header versioning is purer but invisible, and an invisible version is one people get wrong. Query-parameter versioning mixes the contract identity with its parameters.

### Errors: a custom shape vs. RFC 9457 Problem Details
A custom shape means every consumer learns our conventions. RFC 9457 is a standard with existing client support and a defined extension mechanism.

### Pagination: offset vs. cursor
Offset gives page numbers, which administrative interfaces need. Cursor is stable and performs well at depth, which large tables need. Neither alone covers both.

## Decision

**REST over HTTPS with JSON.** Versioning in the URL path: `/api/v1/...`. A published version is additive-only; a breaking change requires a new version, with overlapping support and `Deprecation` / `Sunset` headers.

**Errors use RFC 9457 Problem Details**, extended with `code` (a stable machine-readable identifier), `correlationId`, and a structured `errors` array for field-level validation. `code` never changes and is never localized; `title` and `detail` are human text and are localized from `Accept-Language`. **No error response contains a stack trace, SQL, a connection string or an internal path.**

**Pagination is offset-based by default** (`page`, `pageSize`, max 100), with **cursor pagination additionally offered** on very large collections — audit above all.

**Sorting** uses `sort=field` / `sort=-field`, restricted per endpoint to an allow-list of indexed columns. **Filtering** uses explicit, validated, named parameters per endpoint. **No generic query language in v1** — it is an injection surface and an unbounded performance hazard.

`X-Correlation-Id` is accepted and echoed, generated when absent. `Idempotency-Key` is honoured on POST operations that must not duplicate. All times on the wire are ISO 8601 UTC.

OpenAPI is generated from the code, annotated with permissions and error codes, and **TypeScript types for the frontend are generated from it**.

## Reason

URL versioning wins on operability: when someone reports a problem, the version is in the URL they paste. That is worth more than protocol purity.

RFC 9457 means consumers use an existing standard rather than learning ours, and the extension mechanism lets us add `code` and `correlationId` without leaving the standard. The `correlationId` in every error is what turns "I got an error this morning" into one log query.

Offering both pagination styles rather than choosing one reflects that they serve different needs: administrative UIs need page numbers, and offset paging degrades badly at depth on a table with a hundred million rows.

Restricting sort columns to an indexed allow-list prevents an arbitrary sort parameter from causing a full table scan on a large table — an easy denial-of-service otherwise.

Generating frontend types from OpenAPI means a backend contract change breaks the frontend build. Discovering a contract mismatch at compile time rather than in production is the whole point.

## Consequences

**Positive.** One error shape everywhere, so error handling is written once. Version visible in every log line. Correlation IDs make support tractable. Generated types keep frontend and backend honest. Explicit filters are safe and indexable.

**Negative.** Maintaining two API versions during a deprecation window costs effort. Explicit per-endpoint filters mean more code than a generic query engine, and consumers occasionally want a filter we did not anticipate — which is a deliberate trade against the injection and performance risk. Idempotency key storage needs its own retention.

**Follow-up actions.**
- Implement the shared error mapping, pagination and versioning in the kernel (Phase 1) — before any module writes an endpoint.
- Contract tests asserting the OpenAPI document matches the implementation.
- Publish the versioning and deprecation policy for business application teams (Phase 11).

## Status
Accepted.

