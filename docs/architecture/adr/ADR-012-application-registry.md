# ADR-012: Application Registry & Extensibility Model

| Field | Value |
|---|---|
| Status | **Accepted** |
| Date | 2026-09-06 |
| Deciders | Platform Architecture |

## Context

The Platform must serve business systems that do not exist yet, without knowing anything about them (ADR-005) and without requiring Platform source changes when a new system arrives (P4). Those future systems need to authenticate as themselves, define their own permissions, send audit events attributed to themselves, register workflow definitions and notification templates, and have their calls rate-limited independently.

## Problem

What is the mechanism by which an unknown future system becomes a first-class Platform consumer?

## Options

### Option A — Hardcode each application in Platform configuration
*Pros:* trivial to implement.
*Cons:* every new system requires a Platform change and deployment. The Platform team becomes a bottleneck for every other team, which is the failure mode P4 exists to prevent.

### Option B — An application registry with self-service registration through the API
Applications are data. Each has an identity, credentials, a permission namespace, quotas and a status.

*Pros:* onboarding a new system requires no Platform code change; permissions, templates and workflow definitions are declared by the owning system; every action is attributable to an application.
*Cons:* registration is a privileged operation that must be tightly controlled; a registry is more moving parts than a configuration list.

### Option C — No registry; all systems share one generic API credential
*Cons:* no attribution in audit, no per-application permissions, no per-application quotas, and a single credential whose compromise affects everything. Unacceptable.

## Decision

**Option B.** `Application` is a first-class entity in the Authorization module, carrying: identifier, display name, permission namespace prefix, client credentials (secret stored hashed, shown once), rate-limit quota, allowed scopes, webhook endpoints, and status.

A registered application can:
- authenticate via OAuth 2.0 client credentials (ADR-006);
- **declare its permissions** under its own namespace via a manifest — `finance.invoices.approve` — which the Platform stores and evaluates as an opaque string (ADR-007);
- send audit events attributed to itself, and only to itself;
- register workflow definitions and notification templates;
- subscribe to Platform events by webhook;
- act on behalf of a user, with both identities recorded.

Registration itself requires `platform.applications.manage` and is audited. There is no self-service registration from outside; a human with authority registers an application.

## Reason

This single mechanism resolves the central tension of the whole project: the Platform must be the authority for business systems while knowing nothing about them. It does so by making applications, permissions, workflow definitions and templates *data* rather than *code*. Data can be added without a deployment; code cannot.

It also gives attribution, which matters more than it first appears. Every audit event, every rate limit, every authorization decision can name the application responsible. Without a registry, all machine activity is anonymous and the audit trail loses much of its value.

Registration is deliberately not self-service. Creating a Platform consumer is a privileged act with security consequences, and the convenience of self-service is not worth it for a handful of internal systems.

## Consequences

**Positive.** New systems onboard with no Platform code change. Per-application permissions, quotas, credentials and audit attribution. A compromised application credential is scoped and individually revocable. The Platform team is not a bottleneck for other teams.

**Negative.** The registry is a privileged surface and must be well protected — whoever controls it controls what every system may do. Namespace collisions must be prevented at registration. Credential rotation must work without downtime, which requires supporting two valid secrets during a rotation window. Applications that declare permissions carelessly create authorization sprawl, so registration should include a review of the declared manifest.

**Follow-up actions.**
- Implement the registry in Phase 4 alongside Authorization.
- Complete credentials, quotas and rotation in Phase 11.
- Document the onboarding process for business system teams (Phase 18).
- Enforce namespace uniqueness and a reserved `platform.*` namespace at registration.

## Status
Accepted.

