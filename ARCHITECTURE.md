# Company Central Platform — Architecture

| Field | Value |
|---|---|
| Document | ARCHITECTURE.md |
| Status | **Draft — Phase 0 (Analysis)** |
| Version | 0.1 |
| Last updated | 2026-09-06 |
| Owner | Platform Architecture |
| Language note | Engineering documents are written in English because the codebase, identifiers, API contracts and third-party tooling are English. The **product UI** is fully bilingual (Arabic / English) — see §25. |

> **Source-of-truth notice.** The requirements document referenced in the project brief was **not present** in the repository at the time of this analysis (the working directory was empty). This document is therefore derived from the *Master Development Prompt* supplied by the project owner, which is treated as the authoritative requirements baseline until the original document is provided. Every assumption made in its absence is listed in §27 (Open Architectural Questions) and must be confirmed before Phase 1 begins.

---

## Table of Contents

1. [System Overview](#1-system-overview)
2. [Architecture Principles](#2-architecture-principles)
3. [Platform Scope](#3-platform-scope)
4. [Platform Boundaries](#4-platform-boundaries)
5. [Business Application Boundaries](#5-business-application-boundaries)
6. [Dependency Rules](#6-dependency-rules)
7. [Module Architecture](#7-module-architecture)
8. [Backend Architecture](#8-backend-architecture)
9. [Frontend Architecture](#9-frontend-architecture)
10. [Database Architecture](#10-database-architecture)
11. [API Architecture](#11-api-architecture)
12. [Security Architecture](#12-security-architecture)
13. [Authentication Architecture](#13-authentication-architecture)
14. [Authorization Architecture](#14-authorization-architecture)
15. [Audit Architecture](#15-audit-architecture)
16. [Workflow Architecture](#16-workflow-architecture)
17. [Notification Architecture](#17-notification-architecture)
18. [Document Architecture](#18-document-architecture)
19. [Integration Architecture](#19-integration-architecture)
20. [Cloud Architecture](#20-cloud-architecture)
21. [Deployment Architecture](#21-deployment-architecture)
22. [Observability Architecture](#22-observability-architecture)
23. [Backup & Recovery Strategy](#23-backup--recovery-strategy)
24. [Scalability Strategy](#24-scalability-strategy)
25. [Localization & RTL Architecture](#25-localization--rtl-architecture)
26. [Architecture Governance](#26-architecture-governance)
27. [Open Architectural Questions](#27-open-architectural-questions)

---

## 1. System Overview

The **Company Central Platform** (hereafter *the Platform*) is the company's shared software foundation. It is **not** a business application. It exists so that every current and future business system — Financial, HR, Administrative, IT, Design, Gold, and others not yet imagined — can be built on a single, consistent, secure base instead of re-implementing identity, permissions, auditing, approvals, notifications and document handling for the tenth time.

### 1.1 What the Platform is

A single deployable **modular monolith** (ASP.NET Core, .NET 10 LTS) exposing a versioned REST API, backed by one PostgreSQL database with one schema per module, plus a Next.js administration portal. It provides eleven capability modules and a platform kernel of cross-cutting concerns.

### 1.2 What the Platform is not

It is not an ERP, not an accounting engine, not a transfer system, not a gold trading system, and it holds no business rules belonging to any of those. It does not know what a commission is, what a journal entry is, or how a gold price is calculated.

### 1.3 The system landscape

```
                      ┌──────────────────────────────────────┐
                      │        Human users (browser)         │
                      └───────────────┬──────────────────────┘
                                      │ HTTPS
        ┌─────────────────────────────┼─────────────────────────────┐
        │                             │                             │
┌───────▼─────────┐        ┌──────────▼──────────┐       ┌──────────▼──────────┐
│ Platform Admin  │        │  Financial System   │       │   HR System         │
│ Portal (Next.js)│        │  (future app)       │       │   (future app)      │
└───────┬─────────┘        └──────────┬──────────┘       └──────────┬──────────┘
        │                             │                             │
        │  BFF / server-side          │  machine-to-machine         │
        │  session (httpOnly cookie)  │  (OAuth2 client creds)      │
        │                             │                             │
        └─────────────────────────────┼─────────────────────────────┘
                                      │
                    ┌─────────────────▼──────────────────┐
                    │   COMPANY CENTRAL PLATFORM API     │
                    │        (ASP.NET Core host)         │
                    │                                    │
                    │  Identity · Organization ·         │
                    │  Authorization · Security · Audit ·│
                    │  Workflow · Notifications ·        │
                    │  Documents · Integrations ·        │
                    │  Configuration · Monitoring        │
                    └────┬──────────────┬────────────┬───┘
                         │              │            │
                 ┌───────▼───┐   ┌──────▼─────┐  ┌───▼──────────┐
                 │PostgreSQL │   │  Object    │  │  External    │
                 │ (platform)│   │  Storage   │  │  providers   │
                 └───────────┘   │ (S3-compat)│  │ (SMS, Email) │
                                 └────────────┘  └──────────────┘

        Business applications own their OWN databases. The Platform
        never reads or writes business application data.
```

### 1.4 Primary quality attributes, in priority order

| # | Attribute | Why it ranks here |
|---|---|---|
| 1 | **Security** | The Platform is the single front door to every company system. A breach here is a breach everywhere. |
| 2 | **Business-agnosticism** | If business logic leaks in, the Platform stops being reusable and becomes just another application. |
| 3 | **Maintainability** | This is a decade-long asset maintained by a rotating team, not a project with an end date. |
| 4 | **Testability** | Authentication, authorization and audit must be provably correct, not hopefully correct. |
| 5 | **Scalability** | Must grow with the company, but the company is one organisation — not a public SaaS. Scale honestly. |
| 6 | **Availability** | Every business system depends on it; degradation must be graceful, not total. |
| 7 | **Performance** | Important, but a 40 ms permission check is not worth architectural complexity. |

Note the ordering: **maintainability outranks performance**, and **simplicity outranks scalability**. This ordering is what justifies choosing a modular monolith over microservices.

---

## 2. Architecture Principles

These are binding. A change to any of them requires an ADR.

### P1 — Simple where possible, enterprise where necessary
Complexity must be *earned* by a real requirement. "It looks enterprise", "it's modern", "big companies use it" are not requirements. Every infrastructure component added must name the specific problem it solves and the simpler option it beat.

### P2 — Dependencies point one way
`Business Application → Platform`. Never the reverse. The Platform must be able to compile, test, run and deploy with **zero** knowledge that any specific business application exists.

### P3 — The Platform is business-agnostic
The Platform models *generic* concepts: a user, a permission, an audit event, an approval step, a document. It never models *specific* ones: a transfer, an invoice, a gold lot, a payroll run.

**The test:** if you can describe a Platform feature without naming a single company business process, it belongs. If you cannot, it does not.

### P4 — Extensibility over modification
Future systems extend the Platform through **registration** (register an application, register permissions, register a workflow definition, register a notification template), never by asking for Platform source changes.

### P5 — Modules are logically independent
A module owns its data, exposes a contract, and knows nothing about another module's internals. Any module must be extractable into a separate service later without a rewrite — even though we do not intend to extract any.

### P6 — Security is enforced at the backend
Frontend authorization is a usability feature. Backend authorization is the security control. Every endpoint declares its permission requirement explicitly; there is no "protected by not being linked to".

### P7 — Everything meaningful is audited
Authentication, failed authorization decisions, permission and role changes, configuration changes, document access, and integration calls all produce audit events. Audit is append-only and never edited.

### P8 — No secrets in source control, ever
Not in `appsettings.json`, not in a `.env` that "we'll gitignore later", not in a test fixture, not in a comment, not in this document.

### P9 — Explicit over implicit
Explicit permission attributes, explicit module registration, explicit API versions, explicit error contracts, explicit migrations. Convention-based magic that saves five lines and costs a day of debugging is a net loss.

### P10 — The user interface is a professional instrument, not a showcase
Calm, white and blue, text-first, dense where data is dense, no decoration without function. See §9.

---

## 3. Platform Scope

The Platform owns exactly eleven capability areas plus a kernel.

| Module | Owns | Does not own |
|---|---|---|
| **Identity** | Users, credentials, password policy, sessions, devices, login history, MFA enrolment, account lifecycle | Employee org data, business roles |
| **Organization** | Company, departments, sections, centers, positions, employees, manager relationships | Payroll, attendance, leave balances, performance |
| **Authorization** | Applications registry, permissions, roles, role-permission and user-role assignments, permission evaluation | Which permissions a business app needs |
| **Security** | MFA, lockout, rate limiting, security events, secure headers, token lifecycle, secret access | Business-level fraud rules |
| **Audit** | Append-only audit event store, ingestion API, search, export, retention | Interpreting business meaning of events |
| **Workflow** | Generic workflow definitions, instances, steps, transitions, assignments, SLA timers | Any business approval rule or threshold |
| **Notifications** | Templates, channels, provider abstraction, dispatch, delivery log, user preferences | Deciding when a business event is notable |
| **Documents** | Upload/download, metadata, versioning, access control, storage abstraction, retention | The business meaning of a document |
| **Integrations** | Outbound connector registry, credential vaulting, retry/circuit-breaking, call logging | Business payload semantics |
| **Configuration** | Typed settings by scope, feature flags, change history | Business parameters (rates, fees, prices) |
| **Monitoring** | Health checks, metrics, traces, structured logs, background job status | Business KPIs |

### 3.1 Platform Kernel (cross-cutting, not a module)

Shared building blocks every module uses: result/error model, Problem Details mapping, pagination and filtering contracts, correlation and request IDs, clock abstraction, current-user accessor, domain event dispatcher, transactional outbox, validation pipeline, and the module registration system.

The kernel is deliberately **thin**. It contains no business behaviour and no module-specific knowledge.

---

## 4. Platform Boundaries

### 4.1 Explicitly IN scope

- Central identity — one user account per human being, across all company systems.
- Central organizational structure — one authoritative org chart.
- Central authorization — one place where "who may do what" is defined and evaluated.
- Central audit — one immutable trail across all systems.
- Generic approval workflow — reusable by any system.
- Central notification dispatch — one place channels and providers are configured.
- Central document storage with access control.
- Governed outbound integrations.
- Platform-level configuration and feature flags.
- Platform observability.
- An administration portal for all of the above.

### 4.2 Explicitly OUT of scope

- Any business transaction, calculation, rate, fee, price, balance or ledger.
- Any business-specific entity (transfer, invoice, contract, gold item, payroll run).
- Business reporting and business dashboards.
- Business application databases.
- Being a data warehouse or reporting hub for business data.
- Office network, internal IPs, on-premise servers, firewall configuration (see §20).

### 4.3 The grey zones — decided

Three areas sit uncomfortably on the boundary. They are resolved here so they are not re-litigated later.

**(a) Employees.** An employee record is organizational, not business, data — it answers "who works here, in which department, reporting to whom". That belongs to the Platform. **Payroll, leave, attendance, appraisals and contracts belong to the HR business application**, which references Platform employees by ID. The Platform stores an employee's department and manager; it does not store their salary.

**(b) Workflow.** The *engine* is Platform. The *definitions* are authored by business applications and stored in the Platform as data. A workflow definition saying "Step 1: Review, Step 2: Approve" is generic. A rule saying "amounts above 5,000 require the finance director" is a business rule and must live in the business application, which supplies the routing decision to the engine — the engine never contains the threshold.

**(c) Documents.** Storage, metadata, versioning and access control are Platform. The business classification of a document ("this is a customer KYC file") is a business concern; the Platform stores it only as an opaque tag supplied by the owning application.

### 4.4 The boundary test (apply to every proposed feature)

1. Would a completely different company, in a different industry, need this? → If yes, it may be Platform.
2. Can it be described without naming a company business process? → If no, it is not Platform.
3. Does implementing it require the Platform to know a business rule, rate or threshold? → If yes, it is not Platform.
4. Would three future systems each rebuild it otherwise? → If yes, it should be Platform.

A feature must pass 1, 2 and 3 to enter the Platform.

---

## 5. Business Application Boundaries

A business application (Financial, HR, Gold, …) is a **separate codebase, separate deployment, and separate database**. It is a *consumer* of the Platform.

### 5.1 A business application MUST

- Register itself in the Platform application registry and obtain machine credentials.
- Delegate all authentication to the Platform. It never stores a password or issues its own identity.
- Declare its permissions to the Platform using its own namespace (`finance.*`, `hr.*`).
- Enforce authorization by asking the Platform, or by validating a Platform-issued token carrying its permissions.
- Send its audit events to the Platform audit API.
- Use Platform notification, document and workflow services rather than building its own.
- Route outbound third-party calls through the Platform integration layer.
- Own its business data in its own database, referencing Platform entities by ID only.

### 5.2 A business application MUST NOT

- Create its own user table with credentials.
- Connect directly to the Platform database. **The API is the only contract.**
- Ask for Platform source-code changes to support a business feature.
- Store the org chart, or a private copy of users, beyond a cache with a defined TTL.
- Call external providers directly, outside the integration layer.

### 5.3 The one-way reference rule

```
Business App DB                       Platform DB
─────────────────                     ──────────────────
invoices                              identity.users
  id                                    id  ◄──────────┐
  created_by_user_id  ───────────────────────────────-─┘  (by ID, no FK)
  department_id       ────────────────► organization.departments.id
  amount                                (Platform never sees this column)
  currency                              (Platform never sees this column)
```

There is **no database-level foreign key** across the boundary — the databases are separate. Referential integrity across the boundary is maintained by the application layer, and the Platform guarantees that entity IDs are stable and never reused.

---

## 6. Dependency Rules

### 6.1 Layer dependencies (inside the Platform)

```
        API  ──────────────┐
         │                 │
         ▼                 │
    Application            │
         │                 │
         ▼                 ▼
      Domain  ◄──── Infrastructure
         ▲                 │
         └── Shared/Kernel ┘
```

| Layer | May depend on | May never depend on |
|---|---|---|
| **Domain** | Kernel primitives only | Application, Infrastructure, API, EF Core, ASP.NET Core |
| **Application** | Domain, Kernel, module contracts | Infrastructure implementations, API, EF Core |
| **Infrastructure** | Domain, Application (to implement its interfaces), Kernel | API |
| **API** | Application, Kernel | Domain internals, Infrastructure directly |
| **Kernel** | Nothing in the Platform | Everything |

**Domain has no infrastructure dependencies.** No `DbContext`, no `HttpContext` in domain entities. Persistence concerns are expressed as interfaces in Application and implemented in Infrastructure.

### 6.2 Module dependencies

The rule that makes future extraction possible:

> **A module may reference another module's `.Contracts` project. It may never reference another module's `.Domain`, `.Application` or `.Infrastructure` project.**

```
Modules.Identity.Application  ──may reference──►  Modules.Organization.Contracts   ✅
Modules.Identity.Application  ──may reference──►  Modules.Organization.Domain      ❌ FORBIDDEN
```

A `.Contracts` project contains only: public interfaces, DTOs, integration event records, and enums. No entities, no EF configuration, no business logic.

### 6.3 The allowed module dependency graph

Direct (synchronous, via contracts):

| Module | May call | Rationale |
|---|---|---|
| Identity | — | Foundational; depends on nothing |
| Organization | Identity.Contracts | To link an employee to a user account |
| Authorization | Identity.Contracts, Organization.Contracts | To resolve a user and their org scope |
| Security | Identity.Contracts | To act on accounts (lock, MFA) |
| Audit | — | Must never depend on anything; it must record even a broken system |
| Workflow | Identity, Organization, Authorization (.Contracts) | To resolve assignees |
| Notifications | Identity.Contracts, Integrations.Contracts | To resolve recipients and send |
| Documents | Identity.Contracts, Authorization.Contracts | To enforce access control |
| Integrations | Configuration.Contracts | To read connector settings |
| Configuration | — | Foundational |
| Monitoring | — | Observes, never participates |

Indirect (asynchronous, via domain/integration events — no compile-time dependency): **Audit** and **Notifications** subscribe to events published by any module. This is why Audit has no outbound dependencies — it listens rather than being called.

**Cycles are forbidden.** If two modules appear to need each other, one of two things is true: the responsibility is misplaced, or the interaction should be an event.

### 6.4 Enforcement

Dependency rules are not enforced by good intentions. They are enforced by:
- **Project references** — the compiler rejects a forbidden reference that was never added.
- **Architecture tests** run in CI, asserting the rules in §6.1–§6.3 as executable assertions. A violation fails the build. Implemented in Phase 1 with plain reflection over assembly references rather than a third-party library — the assertions read as the rules themselves and carry no dependency risk.

> **Known limitation** (found in Phase 1): the compiler prunes a reference whose only use is a `const`, because const values are inlined. These tests therefore prove the absence of a *binary* dependency, not of every textual one. Project references are additionally reviewed in pull requests.

See §26.

---

## 7. Module Architecture

### 7.1 Anatomy of a module

Every module has the same five-project shape:

```
src/Modules/<Module>/
├── CCP.Modules.<Module>.Contracts/      ← public surface, referenced by other modules
│   ├── Dtos/
│   ├── Interfaces/
│   └── Events/
├── CCP.Modules.<Module>.Domain/         ← entities, value objects, domain events, rules
├── CCP.Modules.<Module>.Application/    ← use cases, validators, handlers, ports
├── CCP.Modules.<Module>.Infrastructure/ ← DbContext, EF configs, repositories, adapters
└── CCP.Modules.<Module>.Api/            ← endpoints, request/response models, permissions
```

Each module registers itself through a single entry point implementing the kernel's `IPlatformModule` contract, which declares its services, its DbContext, its endpoints, its permissions and its migrations. The host discovers modules explicitly (a registration list, not assembly scanning — see P9).

### 7.2 Module specifications

Each module is specified below in the format the brief requires: Responsibility · Entities · Data · Dependencies · APIs · Events · Extension Points · Security Rules. Entity lists are the **Phase-0 candidate model** and will be refined in each module's own phase.

---

#### 7.2.1 Identity

- **Responsibility.** One authoritative account per human being. Credentials, authentication, session and device lifecycle, login history, MFA enrolment, account state.
- **Entities.** `User`, `UserCredential`, `PasswordHistory`, `Session`, `RefreshToken`, `Device`, `LoginAttempt`, `MfaEnrolment`, `RecoveryCode`, `EmailVerificationToken`, `PasswordResetToken`.
- **Data.** Schema `identity`. Highest-sensitivity data in the Platform. Password hashes only (Argon2id — see §12). Refresh tokens stored hashed, never plaintext.
- **Dependencies.** None.
- **APIs.** `/api/v1/auth/*` (login, logout, refresh, password reset, MFA challenge), `/api/v1/users/*`, `/api/v1/me/*`, `/api/v1/me/sessions`, `/api/v1/me/devices`.
- **Events.** `UserCreated`, `UserDisabled`, `UserEnabled`, `PasswordChanged`, `LoginSucceeded`, `LoginFailed`, `SessionRevoked`, `MfaEnrolled`, `MfaDisabled`.
- **Extension points.** Pluggable password hasher; pluggable external identity provider (future SSO); configurable password policy.
- **Security rules.** Password hashes never leave Infrastructure. No endpoint ever returns a hash, a token, or a recovery code after issuance. Login responses are timing- and message-uniform: an unknown user and a wrong password are indistinguishable. Password reset is enumeration-safe (always returns success).

---

#### 7.2.2 Organization

- **Responsibility.** The authoritative company structure and the employee record as an organizational fact.
- **Entities.** `Company`, `Department`, `Section`, `Center`, `Position`, `Employee`, `EmployeeAssignment`, `ManagerRelationship`.
- **Data.** Schema `organization`. Hierarchies are **adjacency list plus materialized path** — the path column makes "everyone under department X" a single indexed prefix query, which the authorization scope evaluator needs on every request. Depth is not fixed; the model supports arbitrary nesting (the brief forbids assuming a fixed number of departments or centers).
- **Dependencies.** `Identity.Contracts` (an employee may be linked to a user account; not every user is an employee, and not every employee has an account).
- **APIs.** `/api/v1/organization/departments`, `/sections`, `/centers`, `/positions`, `/employees`, `/organization/tree`.
- **Events.** `DepartmentCreated`, `DepartmentMoved`, `EmployeeHired`, `EmployeeTransferred`, `EmployeeDeactivated`, `ManagerChanged`.
- **Extension points.** Custom attributes on employee and department via a typed extension bag, so business apps attach their own metadata without Platform schema changes.
- **Security rules.** Org data is broadly readable inside the company but writable only with `platform.organization.manage`. Moving a department re-computes descendants' paths and **must** invalidate the authorization scope cache.

---

#### 7.2.3 Authorization

- **Responsibility.** Define and evaluate "who may do what". The single source of authorization truth for the whole company.
- **Entities.** `Application` (registry of Platform consumers), `Permission`, `Role`, `RolePermission`, `UserRole`, `PermissionScope`, `DelegatedRole` (time-boxed delegation).
- **Data.** Schema `authz`. Small, extremely hot, aggressively cached.
- **Dependencies.** `Identity.Contracts`, `Organization.Contracts`.
- **APIs.** `/api/v1/roles`, `/api/v1/permissions`, `/api/v1/users/{id}/roles`, `/api/v1/applications`, `/api/v1/authorization/check` (for business apps), `/api/v1/me/permissions`.
- **Events.** `RoleCreated`, `RoleAssigned`, `RoleRevoked`, `PermissionGranted`, `PermissionRevoked`, `ApplicationRegistered`.
- **Extension points.** **Permission registration** — a business application declares its permissions at registration or via a manifest endpoint. This is the mechanism by which the Platform serves systems that do not exist yet without knowing anything about them.
- **Security rules.** No user may grant a permission they do not themselves hold (no privilege escalation by delegation). Role and permission changes are always audited with old and new values. There is exactly one bootstrap super-administrator, created by a documented, audited, one-time procedure — never a hardcoded account.

---

#### 7.2.4 Security

- **Responsibility.** The controls that protect the Platform: MFA, lockout, rate limiting, token hygiene, security event detection, secret access.
- **Entities.** `SecurityEvent`, `LockoutRecord`, `RateLimitPolicy`, `TrustedDevice`, `SecurityPolicy`.
- **Data.** Schema `security`.
- **Dependencies.** `Identity.Contracts`.
- **APIs.** `/api/v1/security/events`, `/api/v1/security/policies`, `/api/v1/security/sessions`, `/api/v1/security/devices`.
- **Events.** `SuspiciousLoginDetected`, `AccountLocked`, `AccountUnlocked`, `MfaChallengeFailed`, `ImpossibleTravelDetected` (later phase).
- **Extension points.** Pluggable MFA methods (TOTP first; SMS and WebAuthn later); pluggable rate-limit stores.
- **Security rules.** Security policy changes require `platform.security.manage` **and** re-authentication (step-up). Security events are never deletable.

---

#### 7.2.5 Audit

- **Responsibility.** One immutable, append-only, company-wide record of what happened. It must record faithfully even when the rest of the system is misbehaving.
- **Entities.** `AuditEvent` (partitioned by month), `AuditExportRequest`.
- **Data.** Schema `audit`. The largest table in the Platform by orders of magnitude. Declaratively partitioned by `occurred_at`. Write-optimized; read paths go through purpose-built indexes only.
- **Dependencies.** **None, by design.** Audit is a sink. It subscribes to events; it never calls a module.
- **APIs.** `POST /api/v1/audit/events` (ingestion, used by business applications), `GET /api/v1/audit/events` (search), `POST /api/v1/audit/exports`.
- **Events.** Consumes; does not publish (except `AuditExportCompleted`).
- **Extension points.** Free-form `metadata` JSONB so any system can attach context without a schema change. Pluggable archival sink for aged partitions.
- **Security rules.** **Append-only** — no UPDATE, no DELETE, enforced by database privileges (the application role is granted INSERT and SELECT only) as well as by the absence of any such code path. Reading audit requires `platform.audit.view`; exporting requires `platform.audit.export` and is itself audited. Old/new values are redacted for fields marked sensitive.

---

#### 7.2.6 Workflow

- **Responsibility.** A generic state machine for multi-step approvals, reusable by any system, containing no business rules.
- **Entities.** `WorkflowDefinition` (versioned), `WorkflowStep`, `WorkflowTransition`, `WorkflowInstance`, `WorkflowTask`, `WorkflowAction`, `WorkflowComment`.
- **Data.** Schema `workflow`. Definitions stored as validated JSON documents; instances stored relationally for queryability.
- **Dependencies.** `Identity.Contracts`, `Organization.Contracts`, `Authorization.Contracts`.
- **APIs.** `/api/v1/workflow/definitions`, `/api/v1/workflow/instances`, `/api/v1/workflow/tasks`, `/api/v1/me/tasks`.
- **Events.** `WorkflowStarted`, `StepEntered`, `TaskAssigned`, `TaskCompleted`, `WorkflowApproved`, `WorkflowRejected`, `WorkflowCancelled`, `WorkflowEscalated`.
- **Extension points.** Assignee resolution strategies (specific user, role, position, requester's manager, department head) — all *organizational*, never business-conditional. Business-conditional routing is provided **by the calling application**, which either supplies the next step or exposes a decision callback. The engine never holds a threshold.
- **Security rules.** Only the assignee (or a delegate) may act on a task. Every action is audited with actor, timestamp and comment. Definition changes are versioned; running instances continue on the version they started with.

---

#### 7.2.7 Notifications

- **Responsibility.** Deliver a message to a person through a configured channel, reliably, with a record.
- **Entities.** `NotificationTemplate` (per locale), `Notification`, `NotificationDelivery`, `NotificationPreference`, `ChannelProvider`.
- **Data.** Schema `notifications`.
- **Dependencies.** `Identity.Contracts`, `Integrations.Contracts`.
- **APIs.** `/api/v1/notifications` (send), `/api/v1/me/notifications` (inbox), `/api/v1/notifications/templates`, `/api/v1/notifications/providers`.
- **Events.** `NotificationQueued`, `NotificationSent`, `NotificationFailed`, `NotificationRead`.
- **Extension points.** **`INotificationChannelProvider`** — the seam that makes new channels additive. In-App and Email in the first implementation; SMS, Push and future channels plug in without touching dispatch logic.
- **Security rules.** A notification is visible only to its recipient. Templates are rendered with contextual escaping — template variables are never trusted. Provider credentials live in the secret store, never in the templates table.

---

#### 7.2.8 Documents

- **Responsibility.** Store a file securely, describe it, version it, and control who may read it.
- **Entities.** `Document`, `DocumentVersion`, `DocumentAccessRule`, `DocumentLink` (polymorphic association to any resource in any system), `DocumentAccessLog`.
- **Data.** Schema `documents` for metadata. **Binary content never enters PostgreSQL** — it goes to S3-compatible object storage.
- **Dependencies.** `Identity.Contracts`, `Authorization.Contracts`.
- **APIs.** `/api/v1/documents` (upload/list), `/api/v1/documents/{id}/content` (download), `/api/v1/documents/{id}/versions`, `/api/v1/documents/{id}/access`.
- **Events.** `DocumentUploaded`, `DocumentVersionAdded`, `DocumentDownloaded`, `DocumentDeleted`.
- **Extension points.** `IDocumentStorageProvider` (local filesystem for development, S3-compatible in production). A scanning hook (`IDocumentScanner`) for malware inspection before a document becomes available.
- **Security rules.** Content-type is determined by **inspecting the file's magic bytes**, never by trusting the client's declared type or extension. An allow-list of permitted types, plus a size limit, both configurable. Downloads are served through short-lived pre-signed URLs or a streaming endpoint that checks permission first — object storage is never publicly readable. Every download is logged.

---

#### 7.2.9 Integrations

- **Responsibility.** Be the only door through which the company's systems talk to the outside world, with governance, credentials, resilience and a log.
- **Entities.** `IntegrationProvider`, `IntegrationEndpoint`, `IntegrationCredential` (a reference to the secret store, not the secret), `IntegrationCallLog`, `WebhookSubscription`.
- **Data.** Schema `integrations`.
- **Dependencies.** `Configuration.Contracts`.
- **APIs.** `/api/v1/integrations/providers`, `/api/v1/integrations/logs`, `/api/v1/integrations/{provider}/health`.
- **Events.** `IntegrationCallFailed`, `CircuitBreakerOpened`, `ProviderHealthChanged`.
- **Extension points.** `IIntegrationConnector` per provider. Resilience (retry with jitter, timeout, circuit breaker, bulkhead) is applied uniformly by the pipeline, not re-implemented per connector.
- **Security rules.** Credentials are stored **only** as references to the cloud secret manager. Outbound targets are constrained to a configured allow-list of hosts. Request and response bodies are logged with sensitive fields redacted by a declared redaction policy per provider.

---

#### 7.2.10 Configuration

- **Responsibility.** Typed, scoped, audited settings and feature flags.
- **Entities.** `SettingDefinition`, `SettingValue` (scoped: platform / application / company), `FeatureFlag`, `SettingChangeHistory`.
- **Data.** Schema `configuration`.
- **Dependencies.** None.
- **APIs.** `/api/v1/configuration/settings`, `/api/v1/configuration/features`.
- **Events.** `SettingChanged`, `FeatureFlagToggled`.
- **Extension points.** Applications register their own setting definitions with type, default, validation rule and sensitivity flag.
- **Security rules.** A setting marked sensitive is write-only through the API and is never returned in a read. Every change records old value, new value, actor and reason. Secrets are **not** configuration — they live in the secret manager (§12.7).

---

#### 7.2.11 Monitoring

- **Responsibility.** Make the Platform's condition observable.
- **Entities.** `HealthCheckResult` (transient), `BackgroundJobRun`, `SystemStatus`.
- **Data.** Schema `monitoring`. Mostly transient; metrics and traces go to the telemetry backend, not the database.
- **Dependencies.** None.
- **APIs.** `/health/live`, `/health/ready`, `/api/v1/monitoring/status`, `/api/v1/monitoring/jobs`.
- **Events.** `HealthDegraded`, `BackgroundJobFailed`.
- **Extension points.** Standard `IHealthCheck` registrations; OpenTelemetry exporters swappable by configuration.
- **Security rules.** `/health/live` is anonymous and returns a bare status with **no** internal detail. `/health/ready` and the detailed status endpoint require authentication and `platform.monitoring.view`.

---

## 8. Backend Architecture

### 8.1 Technology

| Concern | Choice | Note |
|---|---|---|
| Language | C# 14 | |
| Runtime | **.NET 10 (LTS)** | LTS released Nov 2025, supported to Nov 2028. Chosen over the STS release for a decade-long asset. See ADR-002. |
| Framework | ASP.NET Core 10 | Minimal APIs, grouped per module |
| ORM | EF Core 10 | Npgsql provider |
| Database | PostgreSQL 17 | See ADR-004 |
| Validation | FluentValidation | Pipeline behaviour, not endpoint code |
| Mapping | Hand-written mapping | Explicit over implicit (P9); no AutoMapper |
| Resilience | `Microsoft.Extensions.Http.Resilience` (Polly) | |
| Logging | Serilog + OpenTelemetry | |
| Testing | xUnit; architecture rules asserted by reflection (see §6.4); integration tests against a real PostgreSQL database | |

### 8.2 Solution layout

```
CompanyCentralPlatform.slnx            ← .NET 10 solution format
├── src/
│   ├── Host/
│   │   └── CCP.Api.Host/                  ← the single deployable; composition root only
│   ├── Kernel/
│   │   ├── CCP.Kernel/                    ← primitives: Result, Error, IClock, ids, guards
│   │   ├── CCP.Kernel.Application/        ← pipeline behaviours, ICurrentUser, dispatcher
│   │   ├── CCP.Kernel.Infrastructure/     ← outbox, EF conventions, background workers
│   │   └── CCP.Kernel.Api/                ← ProblemDetails, versioning, paging, filters
│   └── Modules/
│       ├── Identity/        (5 projects)
│       ├── Organization/    (5 projects)
│       ├── Authorization/   (5 projects)
│       ├── Security/        (5 projects)
│       ├── Audit/           (5 projects)
│       ├── Workflow/        (5 projects)
│       ├── Notifications/   (5 projects)
│       ├── Documents/       (5 projects)
│       ├── Integrations/    (5 projects)
│       ├── Configuration/   (5 projects)
│       └── Monitoring/      (5 projects)
├── tests/
│   ├── CCP.Architecture.Tests/            ← dependency rules as executable assertions
│   ├── CCP.Kernel.UnitTests/
│   ├── Modules/<Module>.UnitTests/
│   ├── Modules/<Module>.IntegrationTests/ ← Testcontainers PostgreSQL
│   └── CCP.Api.ContractTests/
├── frontend/                              ← Next.js admin portal (see §9)
├── docs/
├── build/                                 ← Dockerfiles, compose, CI scripts
└── tools/
```

The host project contains **no logic** — only configuration, module registration and middleware ordering. If logic appears in the host, it belongs in the kernel or a module.

### 8.3 Request pipeline

```
HTTPS
  → Forwarded headers / HSTS
  → Security headers (CSP, X-Content-Type-Options, Referrer-Policy, Permissions-Policy)
  → Correlation ID (accept inbound or generate; flows to logs, audit, downstream)
  → Request logging (Serilog, sensitive fields redacted)
  → Rate limiting (global + per-endpoint + per-identity)
  → CORS (strict allow-list; no wildcards)
  → Authentication (JWT bearer or BFF session)
  → Authorization (permission requirement handler)
  → Endpoint
      → Validation behaviour (FluentValidation)
      → Handler (Application layer)
      → Transaction + outbox commit (single unit of work)
  → Exception handling → RFC 9457 ProblemDetails
  → Response
```

Two properties of this pipeline matter. Correlation IDs are established **before** anything can fail, so every error is traceable. And the domain event outbox is committed **in the same transaction** as the data change, so an audit event can never be lost after a successful write, nor recorded after a rollback.

### 8.4 Application layer style

Use-case-per-class (command and query handlers). Each handler is small and does one thing. Cross-cutting concerns (validation, logging, transactions, authorization pre-checks, audit) are pipeline behaviours, not repeated code.

**Prohibited by policy:** business logic in endpoints; endpoints longer than a screen; a handler that touches another module's DbContext; a class over ~300 lines without an explicit, documented reason.

### 8.5 Domain events and the outbox

Domain events are raised by entities and dispatched after the transaction commits. Events crossing a module boundary (integration events) are written to a **transactional outbox table** in the same transaction and relayed by a background worker using `SELECT ... FOR UPDATE SKIP LOCKED`, which is safe with multiple application instances. This gives at-least-once delivery; consumers (Audit, Notifications) are idempotent by event ID.

This is the mechanism that lets Audit have zero dependencies while still recording everything.

### 8.6 Background work

No job framework in the initial build. Durable work uses the outbox plus hosted `BackgroundService` workers, which requires no additional infrastructure and is fully testable. A scheduling framework (Quartz.NET) is adopted only when genuine cron-style recurring jobs appear — see ADR-013. This is P1 applied.

---

## 9. Frontend Architecture

### 9.1 Technology (fixed by the project owner — ADR-010)

React · TypeScript · Next.js (App Router) · Tailwind CSS · shadcn/ui. This decision is not open for re-litigation; changing it requires the process in §26.3 and the owner's approval.

| Concern | Choice | Reason |
|---|---|---|
| Framework | Next.js (App Router) | Server components reduce client bundle; route handlers provide the BFF |
| Language | TypeScript, `strict: true` | |
| Styling | Tailwind CSS with **logical properties** | RTL/LTR from one stylesheet (§25) |
| Components | shadcn/ui (source-owned, not a dependency) | We own and restyle the components; see §9.6 |
| Server state | TanStack Query | Caching, invalidation, retries — this is the bulk of app state |
| Forms | react-hook-form + zod | Schemas shared with API contract types |
| Tables | TanStack Table (headless) | Enterprise tables need sorting/filtering/pagination; headless means our own markup |
| Client state | React state / context; Zustand only where proven | §43 of the brief: not everything is global state |
| i18n | next-intl | Message catalogues, locale routing, RTL/LTR direction |
| Testing | Vitest + Testing Library + Playwright | |

### 9.2 Structure

```
frontend/src/
├── app/
│   └── [locale]/
│       ├── (auth)/login, mfa, forgot-password, reset-password
│       └── (portal)/dashboard, users, employees, organization,
│                     roles, permissions, security, audit,
│                     workflow, notifications, documents,
│                     integrations, configuration, monitoring
├── api/                    ← BFF route handlers (server-only, hold the session)
├── components/
│   ├── ui/                 ← design-system primitives
│   ├── layout/             ← shell, sidebar, header, breadcrumbs
│   └── shared/             ← DataTable, FilterBar, PageHeader, EmptyState, ConfirmDialog
├── features/<feature>/
│   ├── api/                ← the ONLY place this feature calls the server
│   ├── components/
│   ├── hooks/
│   ├── schemas/
│   └── types/
├── lib/                    ← http client, auth, permissions, formatting, utils
├── i18n/                   ← ar.json, en.json, config
├── types/                  ← generated API types
└── styles/
```

### 9.3 API communication

```
React component  →  feature hook (TanStack Query)  →  feature API service
                                                             │
                                                             ▼
                                                   shared HTTP client
                                                             │
                                                             ▼
                                          Next.js BFF route handler (server)
                                                             │
                                                             ▼
                                                     Platform REST API
```

No component calls `fetch` directly. No API path string appears in a component. Each feature has exactly one API service module; duplicated request logic is a review failure.

### 9.4 Authentication and authorization on the frontend

The frontend has **no** authentication of its own. It calls Platform APIs (brief §45).

Tokens are held server-side in the Next.js BFF, in an `httpOnly`, `Secure`, `SameSite=Strict` cookie. **No token is ever written to `localStorage` or `sessionStorage`**, which removes the entire class of XSS token theft. See ADR-006.

Authorization on the frontend is **UX only**. A `usePermission()` hook hides or disables controls the user cannot use, so they are not invited to fail. This is convenience, not security — the backend independently enforces every permission (P6). A hidden button is not a protected operation.

### 9.5 Design philosophy — a professional instrument

The interface must read as software commissioned from a professional enterprise design team for people who use it eight hours a day. Concretely:

**Visual identity: white and blue only.**

| Token | Role |
|---|---|
| Surface | white / very light neutral |
| Primary | a calm, desaturated professional blue |
| Text | near-black primary, mid neutral secondary |
| Borders | light neutral, 1px, visible but quiet |
| Status | success / warning / danger **as text and shape, not colour alone** (§42 accessibility) |

Neutral greys are permitted for text, borders and secondary surfaces. No additional accent hues are introduced. No neon, no loud colour, no heavy gradient, no glow, no glassmorphism.

**Forbidden — the "AI look".** No AI illustrations, robots, sparkles, stars, magic or wand imagery, abstract AI graphics, 3D decoration, floating shapes, or decorative elements with no function. No pattern is adopted merely because it is common in AI-generated dashboards.

**Icon policy: no icons by default.** Icons are not decoration. There is no icon beside every nav item, button, table action, card, section or input. Priority goes to typography, text, whitespace, tables, forms, buttons, borders and layout. An icon is permitted only where it demonstrably improves usability or accessibility — and even then used sparingly, never as an icon-only control where a word would be clearer.

> **Practical note for implementation:** shadcn/ui components ship with `lucide-react` icons in their default markup. Every component added to `components/ui/` must have its decorative icons **removed** as part of adoption. Functionally necessary indicators (a select's chevron, a checkbox's check, a sort direction) are the narrow exception.

**Buttons** carry text: *Create User*, *Edit*, *Save*, *Cancel*, *Delete*, *View Details*, *Search*, *Filter*, *Export*. Never make the user guess.

**Tables are the primary interface** for enterprise data: clear, dense, readable, searchable, filterable, sortable, paginated, and comfortable with large datasets. Row actions are text links or a text action menu, not a row of icon buttons.

**Cards are used sparingly**, only where they help. The AI-dashboard pattern of *huge icon + huge number + gradient + description* is forbidden. Use *title → value → supporting information*, quietly.

**Borders and shadows** stay light; whitespace does the work. No heavy elevation, no glow, no glass.

**Border radius** is moderate throughout. This is enterprise software, not a consumer mobile app.

**Animation** serves loading, state change, feedback and open/close only. Short, quiet, and respectful of `prefers-reduced-motion`. No decorative motion.

**Typography** is chosen for readability of dense data — clear numerals, unambiguous digits, comfortable table and form rendering, with an Arabic face of matching quality. No display or expressive fonts.

### 9.6 Component discipline

- Every component that renders user-visible text takes it from the message catalogue. **Hardcoded UI strings are prohibited** (§40 of the brief).
- No huge components. A page composes; it does not implement.
- Shared behaviour lives in `components/shared` — the `DataTable` is written once and used by every list screen.
- Duplicated UI logic and duplicated API logic are both review failures.

### 9.7 Responsive from day one

Desktop, laptop, tablet and mobile are all supported from the first screen, not retrofitted. Tables must not collapse into unusable layouts on a phone: the strategy is horizontal scrolling within a bounded container for dense tables, a card list for the narrowest breakpoints, collapsible filters, and drawer navigation. Every module's screens are tested at all four sizes before its phase is complete.

### 9.8 Accessibility

Semantic HTML, real `<label>` elements, keyboard navigation throughout, visible focus states, form errors associated with their inputs and announced, contrast meeting WCAG 2.2 AA, and screen-reader-compatible tables and dialogs. Colour is never the only carrier of meaning.

---

## 10. Database Architecture

### 10.1 One database, one schema per module

PostgreSQL 17. A single Platform database with a schema per module: `identity`, `organization`, `authz`, `security`, `audit`, `workflow`, `notifications`, `documents`, `integrations`, `configuration`, `monitoring`, plus `kernel` for the outbox.

This gives module data isolation and a clean extraction path, while keeping the operational simplicity of one database — one backup, one connection pool, one migration story, and real transactions across a single module's work.

### 10.2 The cross-schema rule

> **No foreign key crosses a module schema boundary.**

A workflow task referencing a user stores `assignee_user_id UUID` with **no** FK to `identity.users`. Referential integrity across modules is enforced in the application layer via the owning module's contract.

This is the single most important database rule in the Platform. It is what makes P5 (extractability) real rather than aspirational: a schema with no inbound foreign keys can be moved to its own database in an afternoon. A schema tangled in cross-module FKs never can.

The cost is real and accepted: some integrity checks move to application code, and some queries need two round trips instead of one join. That is the price of the boundary, and it is worth paying.

### 10.3 Conventions

| Concern | Convention |
|---|---|
| Naming | `snake_case` for tables and columns; plural table names |
| Primary keys | UUID v7 — time-ordered, so they index well, and safe to expose in URLs (no enumeration) |
| Timestamps | `timestamptz`, always UTC; `created_at`, `updated_at` on every table |
| Actor columns | `created_by`, `updated_by` (user IDs) on every mutable table |
| Concurrency | `xmin` as the concurrency token on entities that admit concurrent edits |
| Money | **`numeric`, never floating point** — and note the Platform stores no business amounts |
| Text | `text` with check constraints, not arbitrary `varchar(n)` limits |
| Enums | Stored as `smallint` or `text` with a check constraint; not PostgreSQL enum types (which are painful to alter) |
| JSON | `jsonb` for genuinely open metadata only — never as a way to avoid modelling |
| Deletes | **Hard delete by default.** Soft delete only where a genuine requirement exists, and then with a partial unique index so "deleted" rows do not block reuse of a natural key |

### 10.4 Integrity is enforced in the database

Not-null, check constraints, unique constraints and foreign keys (within a schema) are declared in the database, not merely in C#. EF Core validation is a usability layer; the database is the guarantee.

### 10.5 Indexing

Indexes are designed alongside each query, not added after a performance incident. Baseline requirements:
- Every foreign key has an index.
- Every `WHERE` clause on a list endpoint is covered.
- Case-insensitive lookups (username, email) use a functional index on `lower(...)` with a matching unique constraint.
- Audit search is served by composite indexes chosen from the actual filter combinations the UI offers, not from every theoretically possible one.
- Unused indexes are a cost; they are reviewed in Phase 17.

### 10.6 Audit partitioning

`audit.audit_events` is range-partitioned by `occurred_at`, one partition per month, created ahead of time by a maintenance job. Retention detaches and archives old partitions rather than deleting rows — detaching a partition is instant, whereas deleting a hundred million rows is an outage.

### 10.7 Migrations

EF Core migrations, one migration history table **per module schema**, so modules version independently. Migrations are reviewed as code, applied by an explicit deployment step (never automatically on application start in production), and must be forward-compatible: deploy the migration, then the code, so a rollback of the application does not break against the new schema. Destructive changes go through an expand-migrate-contract sequence across releases.

### 10.8 Data ownership

The Platform database holds **only** central, shared data. Business data belongs in the business application's own database. If a future request asks the Platform to store business records "just for now", it is refused — that is how platforms turn into monoliths nobody can maintain.

---

## 11. API Architecture

### 11.1 Style and versioning

REST over HTTPS, JSON. Versioning is in the URL path — `/api/v1/...` — chosen for its bluntness: it is visible in logs, in browser address bars, in curl commands and in support conversations. See ADR-008.

A version is additive-only once published. Breaking changes require a new version, and versions overlap for a documented deprecation window announced through the `Deprecation` and `Sunset` headers.

### 11.2 Resource conventions

| Concern | Convention |
|---|---|
| Paths | Plural nouns, kebab-case: `/api/v1/security-events` |
| Methods | GET (safe), POST (create), PUT (full replace), PATCH (partial), DELETE |
| Status | 200, 201 + `Location`, 204, 400, 401, 403, 404, 409, 422, 429, 500 |
| Idempotency | `Idempotency-Key` header honoured on POST for operations that must not be duplicated |
| Correlation | `X-Correlation-Id` accepted and echoed; generated when absent |
| Time | ISO 8601 UTC everywhere on the wire; the client formats for display |

### 11.3 The unified error model — RFC 9457 Problem Details

Every error response, without exception, uses the same shape:

```json
{
  "type": "https://platform.company.com/errors/validation-failed",
  "title": "Validation failed",
  "status": 422,
  "detail": "One or more fields are invalid.",
  "instance": "/api/v1/users",
  "code": "PLATFORM.VALIDATION_FAILED",
  "correlationId": "01JB2X8K9M4N5P6Q7R8S9T0V1W",
  "errors": [
    { "field": "email", "code": "EMAIL_ALREADY_EXISTS", "message": "Email is already registered." }
  ]
}
```

`code` is a stable machine-readable identifier that clients may branch on; `title` and `message` are human text and may be localized. Localization of error text follows the `Accept-Language` header.

**Error responses never leak internals** — no stack traces, no SQL, no connection strings, no internal paths. The `correlationId` is how a user reports a problem and an engineer finds the log.

### 11.4 Collections: pagination, filtering, sorting

Every list endpoint supports all three, consistently:

```
GET /api/v1/users?page=1&pageSize=25&sort=-createdAt&status=active&q=ahmad
```

- **Pagination** is offset-based by default (`page`, `pageSize`, max 100) because administrative UIs need page numbers. Endpoints over very large tables — audit above all — additionally offer **cursor** pagination, because offset paging degrades badly at depth.
- **Sorting**: `sort=field` / `sort=-field`, restricted to an allow-list of indexed columns per endpoint. An arbitrary sort column is an invitation to a table scan.
- **Filtering**: explicit named query parameters per endpoint, validated. No generic query language in v1 — a generic filter engine is an injection surface and a performance hazard.

Response envelope:

```json
{
  "items": [],
  "page": 1,
  "pageSize": 25,
  "totalItems": 1432,
  "totalPages": 58
}
```

### 11.5 Security controls on every endpoint

Authentication by default (`RequireAuthorization` globally; anonymous endpoints are explicitly and rarely opted out). An explicit permission requirement per endpoint. Input validated before the handler runs. Rate limits applied per endpoint class. Response shaping so that internal fields never escape by accident — DTOs are hand-written, never entities serialized directly.

### 11.6 Documentation

OpenAPI generated from the code, published per version, annotated with permissions, error codes and examples. TypeScript types for the frontend are **generated** from the OpenAPI document, so a backend contract change breaks the frontend build rather than production.

---

## 12. Security Architecture

Security is a property of the whole system, not a module. The Security module owns the controls; every layer is responsible for the posture.

### 12.1 Threat model (abbreviated)

The assets worth protecting, in order: credentials and session tokens; the authorization graph (whoever controls roles controls everything); the audit trail (an attacker's first target after entry); personal data of employees; integration credentials; documents.

The main threats considered: credential stuffing and brute force; session or token theft; privilege escalation through role manipulation; insecure direct object reference across the tenant of an org unit; injection; SSRF through the integration layer; malicious file upload; audit tampering; secret leakage through source control or logs.

### 12.2 Defence in depth

| Layer | Controls |
|---|---|
| Transport | TLS 1.2+ only, HSTS with preload, secure cookie attributes |
| Edge | WAF/rate limiting at the platform edge, DDoS protection from the hosting provider |
| Application | Authentication, per-endpoint authorization, validation, output encoding, CSRF protection on cookie-authenticated routes |
| Data | Encryption at rest (provider-managed), Argon2id password hashing, hashed refresh tokens, field-level encryption for the few genuinely sensitive columns |
| Operational | Secret manager, least-privilege database roles, audited administrative access, dependency and container scanning in CI |

### 12.3 Password policy

Argon2id (memory-hard, current best practice; parameters tuned to the production instance and recorded in configuration). Minimum length 12 with no composition theatre, checked against a breached-password list, with password history to prevent reuse. Rate-limited verification. Reset tokens are single-use, short-lived and stored hashed.

### 12.4 Account protection

Progressive lockout on repeated failure — a delay that grows, rather than a hard lock that hands an attacker a denial-of-service tool against real users. Lockouts are per-account and per-IP, both recorded as security events. Notification to the user on lockout, on password change, on MFA change and on a login from a new device.

### 12.5 MFA

TOTP (RFC 6238) first, because it needs no provider and no cost. Recovery codes issued once, stored hashed, shown once. SMS and WebAuthn/passkeys are planned extension points, not first-release work. MFA is enforceable by policy — required for holders of administrative permissions, optional otherwise, configurable.

### 12.6 Rate limiting

ASP.NET Core's built-in rate limiter, with distinct policies: strict on authentication endpoints (per IP and per account), moderate on write endpoints, generous on read endpoints, and a separate quota per registered business application. Exceeding a limit returns 429 with `Retry-After` and raises a security event.

### 12.7 Secrets management

**No secret is ever committed.** Configuration carries *references*; the secret manager carries values.

| Environment | Mechanism |
|---|---|
| Local development | .NET User Secrets, plus a `.env` file that is gitignored from the first commit; a documented `.env.example` with placeholder values only |
| CI | The CI provider's encrypted secret store, masked in logs |
| Staging / Production | The cloud provider's managed secret service, injected as environment variables or fetched at startup; rotation documented |

Enforcement: a `.gitignore` written before the first commit, and **secret scanning in CI** (e.g. gitleaks) that fails the build on a detected credential. Trusting discipline alone is not a control.

### 12.8 Input and output

All input validated at the boundary against an explicit schema. Parameterized queries only — EF Core by default, and any raw SQL uses parameters without exception. Output encoded contextually. Uploads validated by magic bytes, size, and an allow-list. Outbound integration URLs restricted to an allow-list of hosts to prevent SSRF.

### 12.9 Security headers

`Content-Security-Policy` (no `unsafe-inline`, no `unsafe-eval`), `Strict-Transport-Security`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy` denying unused features, `X-Frame-Options: DENY` / `frame-ancestors 'none'`. CORS is a strict origin allow-list; wildcards are prohibited.

### 12.10 Security testing

Automated authorization tests asserting that every protected endpoint rejects an unauthenticated caller and a caller lacking the permission (§55 of the brief). Dependency vulnerability scanning, container image scanning, static analysis, and secret scanning, all in CI. Penetration testing before go-live (Phase 21).

---

## 13. Authentication Architecture

### 13.1 Two kinds of caller

The Platform authenticates **humans** and **applications**, and it does so differently, because the threats differ.

### 13.2 Human authentication — BFF with server-held tokens

```
Browser                Next.js BFF                Platform API
   │                        │                          │
   │── POST /api/login ────►│                          │
   │   (username, password) │── POST /api/v1/auth/login►
   │                        │                          │─ verify Argon2id hash
   │                        │                          │─ check lockout / policy
   │                        │◄─ MFA required ──────────│
   │◄── MFA challenge ──────│                          │
   │── POST /api/login/mfa ►│── POST /auth/mfa/verify ►│
   │                        │◄─ access + refresh ──────│
   │◄─ Set-Cookie: session ─│  (stored server-side)    │
   │   httpOnly Secure      │                          │
   │   SameSite=Strict      │                          │
```

The browser never holds a token. The BFF holds the access and refresh tokens and attaches the access token to upstream calls. This removes XSS token exfiltration as a class of attack, and it is why the BFF pattern is chosen over the simpler "SPA holds a bearer token in memory". See ADR-006.

### 13.3 Tokens

| Token | Form | Lifetime | Storage |
|---|---|---|---|
| Access | JWT, signed RS256 | 15 minutes | Server-side (BFF) or in-memory for machine callers |
| Refresh | Opaque random, 256-bit | 14 days, sliding | **Hashed** in the database, bound to a session and device |

JWT claims are minimal: subject, session ID, issued-at, expiry, issuer, audience, and the permission set **only when it is small enough to stay under header limits**. Otherwise permissions are fetched and cached by the caller — see §14.4.

**Refresh token rotation with reuse detection:** every refresh issues a new refresh token and invalidates the old one. If an already-used refresh token is presented, the entire session family is revoked and a security event is raised — that is the signature of a stolen token.

Signing uses an asymmetric key pair so that business applications can validate tokens locally against a published JWKS endpoint without calling the Platform on every request. Keys are rotated on a documented schedule with an overlap window.

### 13.4 Application authentication — OAuth 2.0 client credentials

A registered business application authenticates with a client ID and secret (secret stored hashed; shown once at issuance) and receives a short-lived access token scoped to that application. Machine tokens carry the application identity, and when acting for a user, the user identity as well — so audit always records *which application, on behalf of whom*.

### 13.5 Sessions and devices

A session records the user, device fingerprint, IP, user agent, creation time and last activity. Users see their own active sessions and may revoke them; administrators may revoke any session. Logout revokes the refresh token server-side — it does not merely delete a cookie, because a token deleted from a browser still works if it was copied.

Absolute session lifetime and idle timeout are both enforced and both configurable.

### 13.6 What is deliberately not built now

Full SSO / external identity provider federation (SAML, OIDC as a relying party) is an extension point in the Identity module, not first-release work. Building a general-purpose identity server when the requirement is "one company, one user directory" would violate P1. The seam is designed; the implementation waits for the requirement.

---

## 14. Authorization Architecture

### 14.1 Model: RBAC with organizational scope

Permissions are grouped into roles; roles are assigned to users. Direct user-permission grants are deliberately **not** supported — they are how permission systems become unauditable. Everything flows through roles.

### 14.2 Permission naming

```
<application>.<resource>.<action>
```

| Example | Meaning |
|---|---|
| `platform.users.view` | View users in the Platform |
| `platform.roles.create` | Create a role |
| `platform.audit.export` | Export audit data |
| `finance.invoices.approve` | (future) Approve an invoice in the Financial System |
| `hr.leave-requests.view` | (future) View leave requests in the HR System |

The application prefix is what keeps the Platform business-agnostic while serving business systems. The Platform owns the `platform.*` namespace and nothing else. A registered application owns its own namespace, declares its permissions through the registration API, and the Platform stores and evaluates them **without understanding what they mean** — `finance.invoices.approve` is, to the Platform, an opaque string attached to a role.

This is the mechanism that satisfies the requirement to serve systems that do not exist yet.

### 14.3 Scope — the dimension RBAC alone cannot express

"View employees" is meaningless without asking *which* employees. Each role assignment therefore carries a scope:

| Scope | Meaning |
|---|---|
| `Self` | Only the user's own records |
| `Unit` | The user's department/section/center |
| `UnitAndBelow` | That unit and everything under it (uses the materialized path from §7.2.2) |
| `All` | Company-wide |

A user may hold the same role at different scopes in different org units. The evaluator returns both the decision and the data filter the query must apply, so scope is enforced at the data level rather than by hoping a caller remembered to filter.

Full ABAC (arbitrary attribute-based policies) is explicitly deferred. RBAC plus organizational scope covers the requirements as stated; ABAC would add a policy engine and a rule language nobody has yet asked for. See ADR-007.

### 14.4 Enforcement

Backend, on every endpoint, declaratively:

```csharp
.RequirePermission("platform.users.create")
```

An ASP.NET Core authorization handler resolves the caller's effective permissions and evaluates the requirement. Effective permissions are cached per user with an explicit version stamp; any change to roles, role permissions or org structure bumps the stamp and invalidates the cache immediately. Stale permissions are a security defect, so invalidation is eager, not TTL-based.

For business applications there are two supported patterns: embed permissions in the access token (fast, no round trip; suitable for small permission sets) or call `POST /api/v1/authorization/check` (always current; suitable for large sets or high-sensitivity operations). Both are documented in the authorization guide.

### 14.5 Invariants

- No user may grant a permission they do not hold.
- No user may modify their own roles.
- The bootstrap administrator is created once by an audited procedure and is never hardcoded.
- Every grant, revoke and role change is audited with old and new values.
- A denied authorization decision on a sensitive resource raises a security event — repeated denials are reconnaissance.
- **Frontend authorization is UX; backend authorization is security.** Both exist; only one is trusted.

---

## 15. Audit Architecture

### 15.1 Purpose

One immutable trail, spanning every company system, answering: who did what, to what, when, from where, and what changed. It must be trustworthy enough to settle a dispute and complete enough to reconstruct an incident.

### 15.2 The event

Every audit event carries, at minimum:

| Field | Notes |
|---|---|
| `id` | UUID v7 |
| `occurred_at` | timestamptz UTC — the partition key |
| `application` | Which system produced it (`platform`, `finance`, `hr`, …) |
| `module` | Module or subsystem |
| `action` | Verb, e.g. `user.created`, `role.assigned` |
| `resource_type`, `resource_id` | What was acted upon |
| `actor_user_id`, `actor_username` | Denormalized name so the trail survives a later rename |
| `on_behalf_of_user_id` | When an application acts for a user |
| `ip_address`, `user_agent`, `device_id` | Origin |
| `request_id`, `correlation_id` | Ties the event to logs and traces |
| `result` | `Success` / `Failure` / `Denied` |
| `old_value`, `new_value` | JSONB, redacted per policy |
| `metadata` | JSONB, open extension for any system |

The actor's username is stored alongside the ID **on purpose**: an audit trail that renders as "user 8f3a… did X" after an employee record changes is not usable evidence.

### 15.3 Ingestion paths

1. **Internal** — modules raise domain events; the audit consumer writes them via the outbox. Automatic, uniform, and impossible to forget.
2. **External** — business applications POST to `/api/v1/audit/events`, authenticated as themselves, with the batch endpoint for volume. Rate-limited and validated; an application may only write events attributed to itself.

### 15.4 Immutability

Append-only, and enforced rather than promised: the application's database role holds `INSERT` and `SELECT` on the audit schema and nothing else. There is no update path in code and no privilege to update in the database. Retention removes whole partitions after archival; it never edits a row.

### 15.5 Reading

Search is filtered by date range (always — an unbounded audit query is an outage), actor, application, module, action, resource and result, served by composite indexes matched to exactly those filters. Export is asynchronous, produces a signed file, and is itself an audited event: exporting the audit trail is a sensitive act.

### 15.6 Reliability

Audit writes must not slow the operation being audited, and must not fail it. Writes go through the outbox: committed transactionally with the change, dispatched asynchronously. If the audit consumer is behind, events queue rather than vanish. If it fails permanently, that is an alert, not a silent loss.

### 15.7 Privacy

Fields declared sensitive are redacted before storage, not after. The Platform never audits a password, a token, a recovery code or a full credential — including in `old_value`/`new_value`, which is where such data most often leaks by accident.

---

## 16. Workflow Architecture

### 16.1 A generic engine, and nothing more

```
Create ──► Review ──► Approval ──► Execution
```

The engine understands states, transitions, assignees, actions and timers. It does not understand what is being approved. Every business rule — thresholds, amounts, eligibility, calculations — belongs to the calling application.

### 16.2 Definitions as data

A workflow definition is a versioned, validated JSON document naming steps, allowed transitions, assignee resolution strategy per step, permitted actions, and optional SLA timers. Applications register definitions through the API. Adding a new approval process for a future system requires **no Platform code change** — this is P4 made concrete.

### 16.3 Assignee resolution — organizational, never business-conditional

Supported strategies: a specific user, any holder of a role, any holder of a position, the requester's direct manager, the head of a given department, or a dynamic list supplied by the calling application at start time.

Note what is absent: there is no "if amount > X then route to Y". Conditional routing driven by business data is resolved by the **calling application**, which either supplies the next step explicitly or exposes a callback the engine invokes to ask "given this instance, what is next?". The engine holds no thresholds, and this is the boundary that keeps the workflow module reusable.

### 16.4 Instances and tasks

Starting an instance records the definition version, the initiating application, the business resource reference (type and ID, opaque to the Platform), and the requester. Each step produces tasks for its resolved assignees. Actions (approve, reject, return, delegate, comment) are recorded with actor, timestamp and comment. Completion or rejection raises an event the calling application subscribes to — via webhook or by polling — so the business system performs its own execution.

The Platform decides *that* something was approved. The business application decides *what that means*.

### 16.5 Guarantees

Instances continue on the definition version they started with, so changing a definition never rewrites history. Transitions are validated against the definition; an action not permitted in the current state is rejected. SLA timers escalate through the background worker. All of it is audited.

---

## 17. Notification Architecture

### 17.1 The chain

```
Business Application / Platform module
                │
                ▼
      Notification Service   ← templates, preferences, dispatch, log
                │
                ▼
      Channel Provider       ← In-App │ Email │ SMS │ Push │ future
                │
                ▼
      External provider (via the Integration layer)
```

### 17.2 The provider seam

`INotificationChannelProvider` is the abstraction that makes channels additive. The dispatcher selects providers by channel and by configuration; adding SMS later means writing one provider and registering it, with no change to dispatch logic, templates or callers. Providers are swappable — replacing one email vendor with another is a configuration change, not a code change.

The first implementation delivers In-App and Email. SMS and Push are designed for and deferred, because building four channel integrations before anyone has asked to send an SMS violates P1.

### 17.3 Templates

Templates are keyed by name and locale, with **Arabic and English versions of every template**, versioned, and rendered with strict escaping. Variables are declared per template and validated at send time, so a missing variable is an error at the boundary rather than an empty space in a message sent to a director.

### 17.4 Delivery

Sending enqueues; it does not block the caller. A background dispatcher delivers through the provider, retries transient failures with exponential backoff and jitter, and records every attempt in a delivery log with status, provider response and timing. Permanently failed notifications are visible in the administration portal rather than silently lost.

Recipients have per-channel, per-category preferences — with the exception of security notifications, which cannot be disabled.

---

## 18. Document Architecture

### 18.1 Split storage

Metadata in PostgreSQL, content in S3-compatible object storage. Binary content never goes in the database: it inflates backups, poisons the connection pool with large transfers, and makes restore times unacceptable.

`IDocumentStorageProvider` abstracts the store — the local filesystem in development, S3-compatible object storage in production (see ADR-014). The abstraction exists so the provider can change without touching the module.

### 18.2 Upload

```
Client → validate size and declared type
       → inspect magic bytes (the declared type is not trusted)
       → check the allow-list
       → compute SHA-256 (integrity and deduplication)
       → optional malware scan hook
       → store content, keyed by a random object key (never the original filename)
       → write metadata + version row
       → raise DocumentUploaded
```

The original filename is stored as metadata only. Using it as a storage key invites path traversal and collisions.

### 18.3 Access control

Every document has an owning application and an owning user, plus explicit access rules. A read requires both the relevant permission and a matching access rule. Object storage is never publicly readable; downloads are served either by a short-lived pre-signed URL issued after the permission check, or by a streaming endpoint that checks first and proxies second. Every download is logged with actor, time and IP.

### 18.4 Versioning and linking

A new upload against an existing document creates a new version; previous versions remain retrievable. `DocumentLink` associates a document with any resource in any system by `(application, resource_type, resource_id)` — the Platform stores the association without understanding the resource, which is what lets a future system attach documents to entities the Platform has never heard of.

### 18.5 Lifecycle

Retention policies per document type; deletion is a two-stage process (mark, then purge after a grace period) so that an accidental deletion is recoverable. Purge removes both the object and the content-addressed metadata, and is audited.

---

## 19. Integration Architecture

### 19.1 One governed door

```
Business Application
        │  (never calls the outside world directly)
        ▼
Platform Integration Layer  ← registry, credentials, resilience, logging, allow-list
        ▼
External provider (bank, SMS gateway, email service, government API, …)
```

Uncontrolled outbound calls scattered across business applications mean credentials scattered across business applications, no shared retry behaviour, no unified log, and no way to answer "what did we send them, and when?". The integration layer exists to make that answerable.

### 19.2 Connectors

Each provider is an `IIntegrationConnector` implementation registered with its endpoints, its credential reference, its timeout and retry policy, and its redaction policy. Resilience is applied by the pipeline uniformly — timeout, retry with exponential backoff and jitter, circuit breaker, and a concurrency bulkhead — so no connector re-implements it and no connector forgets it.

### 19.3 Credentials

Stored as **references** to the cloud secret manager. The database never holds a provider secret, so a database backup leak is not a credential leak. Rotation is a secret-manager operation and requires no deployment.

### 19.4 Logging

Every call records provider, endpoint, correlation ID, request and response with sensitive fields redacted per the connector's declared policy, status, duration and retry count. This log is what makes an integration dispute resolvable. It has its own retention policy, since it grows quickly.

### 19.5 Inbound

Webhooks are received on dedicated endpoints with signature verification, replay protection through a nonce and timestamp window, and strict payload validation. An inbound webhook is untrusted input from the internet and is treated as such.

### 19.6 Failure posture

An external provider being down must degrade one capability, not the Platform. Circuit breakers open, calls queue where the operation permits it, health is reported per provider in the administration portal, and repeated failure raises an alert.

---

## 20. Cloud Architecture

### 20.1 Cloud-first, and what that rules out

The Platform is cloud-first. The company's internal network is treated as **outside our control and outside our design**. The Platform therefore must not depend on any of:

- a local server or on-premise machine
- an internal IP address or internal DNS
- the office network being reachable
- port forwarding
- internal firewall or FortiGate configuration
- any assumption that the development team can change network equipment

The Platform runs on the public internet, secured by TLS, authentication, authorization and rate limiting — not by network topology. This is not a preference; it is a constraint stated in the brief, and it is also better security practice than trusting a perimeter.

### 20.2 Target shape

```
        Internet
           │
     ┌─────▼──────┐
     │ CDN / WAF  │   TLS termination, DDoS protection, edge rate limiting
     └─────┬──────┘
           │
   ┌───────┴────────┐
   │                │
┌──▼───────────┐ ┌──▼──────────────┐
│ Next.js      │ │ Platform API    │   managed container hosting,
│ admin portal │ │ (containers)    │   2+ instances, auto-scaled
└──────────────┘ └──┬──────┬───────┘
                    │      │
       ┌────────────▼──┐ ┌─▼──────────────┐
       │ Managed        │ │ Object storage │
       │ PostgreSQL     │ │ (S3-compatible)│
       │ + replica      │ └────────────────┘
       │ + PITR backups │
       └────────────────┘
                    │
       ┌────────────▼──────────────┐
       │ Secret manager · Telemetry │
       └───────────────────────────┘
```

### 20.3 Managed services, not self-managed infrastructure

PostgreSQL is a **managed** service — backups, patching, replication and point-in-time recovery are the provider's responsibility, not a task the team must remember. Object storage is managed. Secrets are managed. The team's scarce time goes to the Platform, not to operating a database.

### 20.4 What is deliberately not used

**No Kubernetes.** A single modular monolith plus a frontend, serving one company, does not need a cluster orchestrator. Kubernetes would add a control plane, networking, RBAC, manifests and an operational discipline that no requirement calls for, and would consume more engineering time than the entire Monitoring module. Managed container hosting provides the deployment, scaling and health-checking the Platform actually needs. This is P1, and it is explicitly listed in the brief as an architectural prohibition.

**No message broker in the first release.** The transactional outbox in PostgreSQL provides reliable asynchronous delivery for the Platform's own needs. A broker is adopted only when cross-service messaging genuinely exists.

**No separate cache cluster initially.** In-memory caching per instance, with eager invalidation via the version stamp (§14.4), is sufficient at the expected scale. Distributed caching is the first thing added if instance count grows — see §24.

### 20.5 Provider selection

The specific cloud provider is **an open question for the project owner** (§27, Q4). The architecture is deliberately provider-neutral: it requires managed containers, managed PostgreSQL, S3-compatible object storage, a secret manager and an OTLP telemetry endpoint. Every mainstream provider offers all five. Selection should be driven by regional availability, cost, payment practicality for the company, and support quality — not by technical preference. See ADR-009 for the evaluated options.

---

## 21. Deployment Architecture

### 21.1 Environments

| Environment | Purpose | Data | Who deploys |
|---|---|---|---|
| **Local** | Development | Seeded synthetic | Any developer, via Docker Compose |
| **Development** | Shared integration | Synthetic | Automatic on merge to `develop` |
| **Staging** | Pre-production verification; mirrors production configuration | Anonymized or synthetic — **never a production copy with real personal data** | Automatic on merge to `main` |
| **Production** | Live | Real | Manual approval, tagged release |

Production is never used as a development environment. Staging exists so that "it worked locally" is never the last word before go-live.

### 21.2 Local development

`docker compose up` provides PostgreSQL, object storage (MinIO), a mail catcher, and optionally the telemetry collector. The API and frontend run on the host for fast iteration, or in containers for a full-parity check. A single documented command must bring a new developer from clone to running system — see §49 of the brief (Developer Experience).

### 21.3 Containers

Multi-stage Dockerfiles: SDK image builds, runtime image runs. Non-root user. No secrets in images or build args. Pinned base image digests. Images scanned in CI. The same image is promoted through environments — configuration differs, the artifact does not.

### 21.4 CI/CD

```
Push / Pull request
   ↓ Build (API + frontend)
   ↓ Unit tests
   ↓ Integration tests (Testcontainers PostgreSQL)
   ↓ Architecture tests (dependency rules — §6.4)
   ↓ Static analysis + linting + format check
   ↓ Security checks (dependency audit, secret scan, container scan)
   ↓ Package (container images, tagged by commit)
   ↓ Deploy: develop → Development, main → Staging, tag → Production (manual approval)
```

GitHub Actions. The pipeline exists from **Phase 1**, not Phase 18 — a pipeline added late is a pipeline that never ran on the code that needed it most. See §PROJECT_PLAN deviations.

### 21.5 Database migrations in deployment

Migrations run as an **explicit deployment step**, never automatically at application start. Automatic startup migration in a multi-instance deployment means several instances racing to alter the same schema. The sequence is: back up, apply migration, verify, deploy application, verify, and keep the previous image ready for rollback.

Migrations are forward-compatible so that the previous application version still runs against the new schema for the duration of a rolling deployment.

### 21.6 Release and rollback

Rolling deployment with health checks; an instance joins the load balancer only after `/health/ready` passes. Rollback of the application is redeploying the previous image. Rollback of a *migration* is not assumed to be possible — which is precisely why destructive schema changes use expand-migrate-contract across releases rather than a single destructive migration.

---

## 22. Observability Architecture

### 22.1 Three signals, one correlation ID

Logs, metrics and traces, all carrying the same `correlation_id` and `request_id` that the audit trail carries. This is what turns "a user reported an error at 10:14" into a single query.

### 22.2 Logging

Serilog, structured, JSON to stdout (the container platform collects it). Levels used honestly: `Error` for things needing attention, `Warning` for things that are unusual, `Information` for meaningful business-neutral events, `Debug` off in production.

**Never logged:** passwords, tokens, refresh tokens, recovery codes, secrets, full personal identifiers. A redaction policy is applied at the sink, not left to the discipline of whoever writes the log statement.

Logs are for engineers; audit is for the record. They are different systems with different guarantees, and one is not a substitute for the other.

### 22.3 Metrics and tracing

OpenTelemetry instrumentation, exported over OTLP to whichever backend the chosen provider offers. Baseline metrics: request rate, error rate and duration percentiles per endpoint; database connection pool usage and query duration; outbox depth and dispatch lag; background job success and failure; authentication successes and failures; rate-limit rejections.

Distributed tracing is instrumented from the start even though there is a single service, because traces through the pipeline, EF Core and outbound HTTP are the fastest way to find a slow request — and because business applications calling the Platform will propagate trace context.

### 22.4 Health checks

`/health/live` — is the process alive? Anonymous, no detail, used by the container platform.
`/health/ready` — can it serve? Checks database, object storage and critical dependencies. Authenticated detail; a bare status for the load balancer.

A failing dependency degrades readiness so traffic routes away, rather than accepting requests it cannot serve.

### 22.5 Alerting

Alerts are defined for conditions a human must act on: error rate above threshold, readiness failing, outbox lag growing, background job failures, authentication failure spikes, rate-limit surges, certificate expiry, and disk or connection exhaustion. Alerts that fire routinely and are routinely ignored are worse than no alerts and are removed.

### 22.6 Complexity discipline

Observability is built with the managed OTLP backend the cloud provider offers. Self-hosting a Prometheus, Grafana, Loki and Jaeger stack for one application is exactly the kind of "looks enterprise" complexity P1 forbids.

---

## 23. Backup & Recovery Strategy

### 23.1 Objectives

| Metric | Target |
|---|---|
| **RPO** (maximum acceptable data loss) | 5 minutes |
| **RTO** (maximum acceptable downtime) | 4 hours |

These are proposed targets and require the project owner's confirmation (§27, Q7) — they determine cost, and cost is not an engineering decision.

### 23.2 What is backed up

| Asset | Method | Retention |
|---|---|---|
| PostgreSQL | Managed automated backups + continuous WAL archiving for point-in-time recovery | 30 days PITR, 12 monthly full backups |
| Object storage (documents) | Versioning enabled + cross-region replication | Versions 90 days; replication continuous |
| Secrets | Managed service's own backup + a documented, offline-sealed break-glass copy | — |
| Configuration | In source control (it is code) + database backup for runtime settings | Git history |
| Container images | Registry retention | Last 30 releases |

### 23.3 Recovery procedures

Documented in `docs/deployment/` with exact commands, for each of: point-in-time restore of the database; restore of a single document version; full environment rebuild from scratch; secret compromise (rotate, revoke, audit); and accidental destructive migration.

### 23.4 Testing the restore

> A backup that has never been restored is a hypothesis, not a backup.

Restore drills are scheduled quarterly into a temporary environment, timed against the RTO, and their results recorded. The first drill is a Phase 17 acceptance criterion — before go-live, not after the first incident.

### 23.5 Disaster recovery

The recovery strategy is a documented rebuild from infrastructure-as-code plus backups into an alternate region, with DNS cut-over. A warm standby is not provisioned initially because the RTO does not require it; if the owner tightens the RTO, this decision is revisited with its cost attached.

---

## 24. Scalability Strategy

### 24.1 Honest scale

This Platform serves one company. The realistic ceiling is thousands of users, tens of concurrent business applications, and an audit table that grows into the hundreds of millions of rows over years. It is not a public SaaS, and designing for a scale that will never arrive is how projects die of complexity.

The audit table is the only component with a genuinely large-data problem, and it is addressed directly (partitioning, §10.6).

### 24.2 Scaling order — cheapest effective step first

1. **Vertical** — a larger instance. Boring, immediate, and usually sufficient.
2. **Horizontal (stateless API)** — the API holds no session state in memory, so instances scale out behind the load balancer without change. This is designed in from day one even though it will not be needed for a long time; it costs nothing now and cannot be retrofitted cheaply.
3. **Read replicas** — route reporting and audit search to a replica.
4. **Distributed cache** — when multiple instances make per-instance caching wasteful.
5. **Partitioning and archival** — already planned for audit; extended to integration logs and notification deliveries.
6. **Module extraction** — the last resort, made possible by §6.2 and §10.2. Not planned, and hopefully never needed.

### 24.3 Statelessness requirements

No in-process session state, no in-memory job scheduling that assumes a single instance, no local file storage for anything that must survive a restart, and background workers that coordinate through the database (`FOR UPDATE SKIP LOCKED`) rather than assuming they are the only one running.

These are cheap constraints today and the difference between a ten-minute scale-out and a rewrite later.

### 24.4 Performance budgets

Indicative targets, to be validated in Phase 20: authentication under 300 ms p95; a permission check under 20 ms p95 (cached); a typical list endpoint under 400 ms p95; an audit search under 2 s p95. Budgets exist so that a regression is a failed test, not an opinion.

---

## 25. Localization & RTL Architecture

The Platform is fully bilingual: **Arabic (RTL)** and **English (LTR)**, as equals. Arabic is not a translation layer applied to an English product.

### 25.1 Rules

- **No hardcoded UI text**, anywhere, without exception. Every string comes from a message catalogue.
- Locale is a route segment (`/ar/...`, `/en/...`), so a page is linkable and shareable in a specific language.
- `<html lang dir>` is set from the locale; the entire layout mirrors, not merely the text.
- Tailwind **logical properties** (`ms-`, `me-`, `ps-`, `pe-`, `start-`, `end-`) replace `left`/`right` throughout. This is a code-review rule: a physical directional utility in a component is a defect.
- Numbers, dates and times are formatted with `Intl`, per locale, and stored as UTC.

### 25.2 What must mirror

Navigation, sidebar, forms, tables (including column order and alignment), pagination, dialogs, dropdowns, breadcrumbs, spacing, alignment, icons that imply direction, and progress or step indicators. Each is verified in both directions as part of every frontend phase's acceptance criteria — not in a single "RTL pass" at the end, which is how RTL support becomes permanently half-finished.

### 25.3 Backend localization

API error `title` and `message` are localized from `Accept-Language`; `code` never is. Notification templates exist per locale. Data entered by users (a department name) is stored in both languages where the domain requires it — `name_ar` and `name_en` — rather than being translated at render time.

---

## 26. Architecture Governance

### 26.1 Fitness functions — rules that fail the build

Architecture that is documented but not enforced decays. These rules run in CI:

| Rule | Enforced by |
|---|---|
| Domain does not reference EF Core, ASP.NET Core or Infrastructure | Architecture test |
| A module does not reference another module's Domain/Application/Infrastructure | Architecture test |
| No module dependency cycles | Architecture test |
| No cross-schema foreign keys | Migration review + a schema assertion test |
| Every endpoint declares a permission or is explicitly marked anonymous | Architecture test |
| No secret patterns in the repository | Secret scanning |
| No `left`/`right` physical Tailwind utilities in components | Lint rule |
| No hardcoded user-facing strings | Lint rule |
| Public API changes match the OpenAPI contract | Contract test |

### 26.2 Review checklist

Every pull request is checked against: does it respect the module boundary; does it put business logic where it belongs; is the endpoint authorized and audited; is input validated; are secrets absent; are tests present; is UI text localized; does the UI follow the design and icon policy; is the change documented.

### 26.3 Changing an architectural decision

When a decision proves wrong — and some will — the process is: identify the problem plainly, explain the impact, evaluate alternatives, write an ADR superseding the previous one, obtain the owner's approval for owner-level decisions (frontend stack, cloud provider, database, boundaries), update this document, implement, and test. Problems are not hidden and decisions are not changed silently.

---

## 27. Open Architectural Questions

These require the project owner's input. Recommendations are given; none is decided unilaterally.

| # | Question | Why it matters | Recommendation |
|---|---|---|---|
| **Q1** | **The referenced requirements document was not found in the repository.** Does it exist? | This entire analysis is derived from the Master Prompt. A real document may contain constraints, integrations or scope that change the plan. | Provide the document before Phase 1. If none exists, confirm the Master Prompt as the baseline in writing. |
| **Q2** | Single company, or must the Platform support multiple legal entities? | Affects the Organization model and every scoped query. Retrofitting is expensive. | Assume **one company** (the brief says "Company Settings", singular). The `Company` root entity is already present, so supporting several later relaxes a constraint rather than reshaping the schema. |
| **Q3** | Expected scale: users, business applications, audit events per day? | Drives instance sizing, partition strategy and caching. | Assume 500–2,000 users, under 10 applications, under 1M audit events/day. Confirm. |
| **Q4** | Which cloud provider? | Determines managed service names, secret manager, telemetry backend and cost. | Decide before Phase 19; the architecture stays provider-neutral until then. See ADR-009. |
| **Q5** | Is external SSO (Microsoft 365 / Google Workspace) required, now or later? | An extension point is cheap to design now, expensive to add after Identity ships. | Design the seam in Phase 2; implement only on request. |
| **Q6** | Data residency or regulatory constraints (where may data be stored)? | Constrains provider and region choice, and may forbid cross-region replication. | Must be answered before Q4. |
| **Q7** | Confirm RPO 5 min / RTO 4 hours. | Directly determines infrastructure cost. | Accept the proposal, or state the real tolerance. |
| **Q8** | Audit retention period, and any legal minimum? | Determines partition retention and storage cost. | Propose 7 years archived, 12 months hot. Confirm. |
| **Q9** | Does the Platform notify by SMS in the first release? | Decides whether an SMS provider integration is in scope early. | Recommend In-App + Email first; SMS when a real need appears. |
| **Q10** | Who is the bootstrap administrator, and by what procedure? | A security-critical, one-time operation that must not be improvised. | Document the procedure in Phase 4 and execute it with the owner present. |
| **Q11** | Is there an existing user directory or employee data to migrate? | A migration path is a phase of its own if so. | Answer before Phase 2 and Phase 3. |
| **Q12** | Preferred Arabic typeface, and does the company have a brand blue? | Affects the design system tokens set in Phase 7. | Answer before the frontend phase; a sensible default is chosen otherwise. |

---

## Appendix A — Glossary

| Term | Meaning here |
|---|---|
| **Platform** | The Company Central Platform — this system |
| **Business Application** | A separate system built on the Platform (Financial, HR, Gold, …) |
| **Module** | A bounded capability inside the Platform with its own schema and contract |
| **Kernel** | Shared cross-cutting building blocks, containing no business behaviour |
| **Contract** | A module's public surface: interfaces, DTOs, events — the only thing other modules may reference |
| **BFF** | Backend-for-Frontend — the Next.js server layer that holds session tokens |
| **Outbox** | A table written in the same transaction as a change, relayed asynchronously |
| **Scope** | The organizational reach of a permission (Self / Unit / UnitAndBelow / All) |
| **Fitness function** | An automated test that fails the build when an architecture rule is violated |

---

## Appendix B — Decision index

| ADR | Title | Status |
|---|---|---|
| ADR-001 | Architecture Style — Modular Monolith | Accepted |
| ADR-002 | Backend Technology — .NET 10 LTS / ASP.NET Core | Accepted |
| ADR-003 | Frontend Technology — Next.js / React / TypeScript | Accepted (owner-mandated) |
| ADR-004 | Database Technology — PostgreSQL | Accepted |
| ADR-005 | Platform / Business Boundary | Accepted |
| ADR-006 | Authentication Strategy — JWT + rotating refresh, BFF for browsers | Accepted |
| ADR-007 | Authorization Strategy — RBAC with organizational scope | Accepted |
| ADR-008 | API Strategy — REST, URL versioning, RFC 9457 errors | Accepted |
| ADR-009 | Cloud Strategy — managed services, no Kubernetes | Accepted (provider pending) |
| ADR-010 | Frontend Design System — white/blue, text-first, no icons by default | Accepted (owner-mandated) |
| ADR-011 | Localization & RTL Strategy | Accepted |
| ADR-012 | Application Registry & Extensibility Model | Accepted |
| ADR-013 | Background Jobs — outbox and hosted workers | Accepted |
| ADR-014 | Document Storage — object storage, not the database | Accepted |
| ADR-015 | Observability — OpenTelemetry with a managed backend | Accepted |
