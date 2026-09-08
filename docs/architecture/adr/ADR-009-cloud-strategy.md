# ADR-009: Cloud Strategy — Managed Services, No Kubernetes

| Field | Value |
|---|---|
| Status | **Accepted** (provider selection pending) |
| Date | 2026-09-06 |
| Deciders | Project Owner (provider), Platform Architecture |

## Context

The brief is explicit and unusually clear: the Platform is cloud-first; the company's internal network is outside our control; the Platform must not depend on a local server, an internal IP, the office network, port forwarding, or internal firewall or FortiGate configuration; and the development team must not be assumed to have access to network equipment. Kubernetes is explicitly prohibited without need, as is adding infrastructure that merely looks enterprise.

The system to deploy is one API application, one frontend application, a database, object storage, secrets and telemetry.

## Problem

What deployment infrastructure serves this system without adding complexity nobody asked for?

## Options

### Option A — Kubernetes (managed or self-hosted)
*Pros:* the industry standard for container orchestration; scales to anything.
*Cons:* a control plane, cluster networking, ingress controllers, RBAC, manifests or Helm charts, and an operational discipline — for two applications. Operating it would consume more engineering time than building the Monitoring module. Explicitly prohibited by the brief, and rightly so at this scale.

### Option B — Managed container hosting (Azure Container Apps / AWS App Runner or ECS Fargate / Google Cloud Run / an equivalent)
*Pros:* deploys a container, scales it, health-checks it, and routes traffic to it — which is the entire requirement; no cluster to operate; scales to zero or near-zero in non-production environments.
*Cons:* less control; some provider lock-in in the deployment configuration (though not in the application).

### Option C — Virtual machines with Docker Compose
*Pros:* simple; cheap; full control.
*Cons:* the team becomes responsible for OS patching, TLS renewal, log rotation, backups and monitoring — all of which is undifferentiated work, and all of which is the first thing skipped when the team is busy.

### Option D — Platform-as-a-Service (App Service / Heroku-style)
*Pros:* simplest deployment; managed runtime.
*Cons:* less container flexibility; can be more expensive at scale. A reasonable alternative to B for this workload.

## Decision

**Option B — managed container hosting**, with managed PostgreSQL (automated backups, replica, point-in-time recovery), S3-compatible object storage, a managed secret manager, and a managed OTLP telemetry backend.

**No Kubernetes. No message broker in the first release** — the transactional outbox in PostgreSQL covers the Platform's asynchronous needs (ADR-013). **No separate cache cluster initially** — per-instance in-memory caching with eager invalidation suffices; a distributed cache is the first addition if instance count grows.

**The specific cloud provider is deferred to the project owner** (open question Q4), and must be decided together with data residency (Q6). The architecture is provider-neutral: it requires managed containers, managed PostgreSQL, S3-compatible object storage, a secret manager and an OTLP endpoint. Every mainstream provider offers all five.

## Reason

The requirement is to run two containers reliably on the internet. Managed container hosting does exactly that. Kubernetes does that too, plus a great deal else that nobody needs, at a permanent cost in operational attention — this is the clearest possible case for P1.

Managed data services are chosen over self-managed for a specific reason: backups, patching and point-in-time recovery are things a small team *intends* to do and, under pressure, does not. Making them the provider's responsibility is not laziness; it is the difference between having backups and believing you have backups.

Provider selection is genuinely the owner's decision, not an engineering one — it depends on regional availability, cost, payment practicality for the company and support quality. Keeping the architecture provider-neutral means this decision can be made late without rework.

## Consequences

**Positive.** No cluster to operate. Backups, patching and TLS are the provider's responsibility. Scaling is a configuration value. Non-production environments cost little. The team's time goes to the Platform.

**Negative.** Deployment configuration is provider-specific, so changing providers means rewriting deployment configuration (though not the application). Managed services cost more per unit of compute than raw VMs — accepted, because the operational time saved costs more than the difference. Less low-level control.

**Follow-up actions.**
- **Resolve Q4 (provider) and Q6 (data residency) before Phase 19.** Q6 must be answered first, as residency constrains provider and region.
- Keep the application free of provider-specific SDKs where a standard abstraction exists (S3-compatible API, OTLP, standard secret injection) so the architecture stays neutral in practice, not just on paper.
- Revisit only if a measured requirement — not a preference — demands more control.

## Status
Accepted; provider pending.

