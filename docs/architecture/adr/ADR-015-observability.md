# ADR-015: Observability — OpenTelemetry with a Managed Backend

| Field | Value |
|---|---|
| Status | **Accepted** |
| Date | 2026-09-06 |
| Deciders | Platform Architecture |

## Context

Every company system will depend on the Platform, so when something is wrong, finding out what — quickly — matters. The brief requires monitoring of application, API and database health, errors, request duration, background jobs, security events and system status, with an architecture that can accommodate OpenTelemetry, metrics, tracing and centralized logging **without unnecessary complexity**.

That final clause is the constraint that shapes this decision.

## Problem

How is the Platform made observable without building an observability platform?

## Options

### Option A — A self-hosted stack (Prometheus + Grafana + Loki + Jaeger)
*Pros:* powerful; no per-ingestion cost; full control.
*Cons:* four more systems to deploy, secure, back up and upgrade — for one application. The monitoring infrastructure would be more operationally complex than the thing it monitors, and would itself need monitoring. Exactly the "looks enterprise" complexity P1 forbids.

### Option B — OpenTelemetry instrumentation exported to the cloud provider's managed backend
*Pros:* vendor-neutral instrumentation in the code; nothing to operate; traces, metrics and logs correlated out of the box; changing backend later means changing an exporter endpoint, not re-instrumenting.
*Cons:* per-ingestion cost; some provider lock-in in dashboards and alert definitions (not in the instrumentation).

### Option C — Logging only, no metrics or tracing
*Pros:* simplest.
*Cons:* no latency percentiles, no dependency visibility, no way to answer "which part of this request was slow" without guessing. Insufficient for the Platform's role.

## Decision

**Option B.** OpenTelemetry instrumentation for traces, metrics and logs, exported over OTLP to the managed backend of the chosen cloud provider (ADR-009). Serilog for structured logging, written as JSON to stdout for the platform to collect.

Baseline signals: request rate, error rate and duration percentiles per endpoint; database connection pool usage and query duration; outbox depth and dispatch lag; background job success and failure; authentication successes and failures; rate-limit rejections.

**One correlation ID** flows through logs, traces and the audit trail, so a single identifier retrieves all three for a request.

Health checks: `/health/live` (anonymous, no internal detail) and `/health/ready` (dependency checks; detail requires authentication).

**Never logged:** passwords, tokens, refresh tokens, recovery codes, secrets, or full personal identifiers — enforced by a redaction policy applied at the sink, not left to whoever writes each log statement.

Alerts are defined only for conditions a human must act on. Alerts that fire routinely and are routinely ignored are removed.

## Reason

OpenTelemetry keeps the instrumentation vendor-neutral, which is what makes the managed backend a safe choice: if the provider or the cost changes, we change an exporter endpoint rather than re-instrumenting the application.

Distributed tracing is instrumented from the start despite there being a single service, for two reasons: traces through the pipeline, EF Core and outbound HTTP are the fastest way to find a slow request even within one process; and business applications calling the Platform will propagate trace context, so the capability will be needed as soon as the first one exists.

Redaction at the sink rather than at the call site is a deliberate choice. Relying on every developer to remember not to log a token works until one forgets, and the failure is silent and permanent — the secret is in the log archive.

## Consequences

**Positive.** Full visibility with nothing to operate. One correlation ID ties logs, traces and audit together, which is the difference between diagnosing an incident in minutes and in hours. Vendor-neutral instrumentation. Native .NET support, so instrumentation is configuration rather than code.

**Negative.** Ingestion cost, which grows with traffic and must be managed through sampling and log-level discipline. Dashboards and alert definitions are provider-specific and would need rebuilding on a provider change. Trace sampling means not every request is captured, so a rare issue may not have a trace.

**Follow-up actions.**
- Structured logging, correlation IDs and health checks in Phase 1 — before any module exists.
- Full OpenTelemetry metrics and tracing in Phase 14.
- Redaction policy implemented and tested in Phase 1, extended as modules are added.
- Measure telemetry cost in staging before go-live and set sampling accordingly (Phase 19).
- Define alerts in Phase 14 and verify each fires by simulating its condition.

## Status
Accepted.
