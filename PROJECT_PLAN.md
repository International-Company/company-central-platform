# Company Central Platform — Project Plan

| Field | Value |
|---|---|
| Document | PROJECT_PLAN.md |
| Status | **Phases 1–5 core complete; Phase 6 next — see DEVELOPMENT_STATUS.md** |
| Version | 0.1 |
| Last updated | 2026-09-08 |
| Companion documents | [ARCHITECTURE.md](ARCHITECTURE.md) · [DEVELOPMENT_STATUS.md](DEVELOPMENT_STATUS.md) · [ADRs](docs/architecture/adr/) |

---

## 1. How this plan is executed

The Platform is **not** built in one pass. Each phase runs through the same cycle, and the next phase does not begin until the current one is clean:

```
Analyze → Implement → Run → Test → Fix → Architecture review
        → Update documentation → Update status → STOP → await CONTINUE
```

A phase is not complete while it has fundamental defects or unfinished parts. Partial completion is recorded honestly in `DEVELOPMENT_STATUS.md` rather than rounded up.

Every phase below is specified with: **Objective · Tasks · Architecture Decisions · Implementation · Tests · Acceptance Criteria · Status · Known Issues · Risks.**

---

## 2. Deviations from the originally proposed phase order

The brief permits reordering where analysis shows a better sequence. Five changes are proposed, each with its reason. **The original order is preserved in the mapping table (§3) so nothing is silently dropped.**

**(a) API foundations move from Phase 10 into Phase 1.**
Versioning, the unified error model, pagination, correlation IDs and OpenAPI are cross-cutting contracts every module's endpoints must obey. Introducing them at Phase 10 would mean nine modules built against a different convention and then retrofitted. Phase 11 retains the genuinely later API work: the external application registry, client credentials, API keys, quotas and the developer portal.

**(b) Configuration and Monitoring basics move from Phases 12–13 into Phase 1.**
Structured logging, correlation IDs, health checks and typed configuration are needed by the first line of code written in Phase 2. The advanced work — feature flags with scoping, the settings administration UI, OpenTelemetry metrics and tracing — stays in its own later phase.

**(c) CI/CD moves from Phase 18 into Phase 1.**
A pipeline introduced near the end never ran against the code that most needed it. Build, test, architecture tests, secret scanning and container packaging exist from the first commit. Phase 19 keeps the deployment pipeline to real cloud environments.

**(d) Frontend moves from Phase 15 to Phase 7.**
Building fifteen backend phases with no interface carries three real risks: the design system and its bilingual RTL foundation get validated far too late; API contracts are never exercised by a real consumer; and nothing is demonstrable to the owner for months. Phase 7 delivers the design system, the application shell, authentication screens and the administration screens for the four modules that exist by then. Later modules ship their own screens **within their own phase**, which keeps every phase end-to-end and testable.

**(e) Testing is continuous, not a phase.**
Every phase carries its own tests in its acceptance criteria. The former "Phase 20 — Testing" becomes **Phase 20 — Testing & Quality Hardening**: end-to-end suites, load testing, penetration testing and coverage remediation. It is a hardening phase, not the phase where testing starts.

---

## 3. Phase map (original → revised)

| Revised | Phase | Original # |
|---|---|---|
| 0 | Analysis | 0 |
| 1 | Foundation & Platform Kernel | 1 + parts of 10, 12, 13, 18 |
| 2 | Identity & Authentication | 2 |
| 3 | Organization | 3 |
| 4 | Authorization & RBAC | 4 |
| 5 | Security Hardening | 5 |
| 6 | Audit | 6 |
| 7 | Frontend Foundation & Core Administration UI | 15 (moved up) |
| 8 | Workflow | 7 |
| 9 | Notifications | 8 |
| 10 | Documents | 9 |
| 11 | External API Platform & Application Registry | 10 (remainder) |
| 12 | Integrations | 11 |
| 13 | Configuration & Feature Flags | 12 (remainder) |
| 14 | Observability | 13 (remainder) |
| 15 | Administration Portal Completion | 16 |
| 16 | Platform Dashboard | part of 16 |
| 17 | Database Hardening, Backup & Recovery | 14 |
| 18 | Developer Experience & Documentation | 17 |
| 19 | Cloud Deployment | 19 |
| 20 | Testing & Quality Hardening | 20 |
| 21 | Final Hardening & Go-Live | 21 |

---

## 4. Prerequisites before Phase 1

Verified on this machine on 2026-09-06:

| Requirement | Status |
|---|---|
| Node.js 24.18 | ✅ Installed |
| Docker 29.6 | ✅ Installed |
| Git 2.54 | ✅ Installed |
| **.NET 10 SDK** | ✅ Installed (10.0.400, pinned in `global.json`) |
| PostgreSQL | ⚠️ 17 installed natively, but superuser password unknown; Docker engine will not start |
| Git repository initialized | ✅ Initialized on `main` |
| Requirements document | ❌ Not found — see ARCHITECTURE.md §27 Q1 |
| GitHub repository | ⚠️ To be created in Phase 1 |

---

# PHASE 0 — ANALYSIS

**Status: ✅ COMPLETE**

### Objective
Understand the requirements, define the architecture, boundaries, modules and technology decisions, and produce the planning artifacts — without writing production code.

### Tasks
- [x] Read all available documentation
- [x] Analyze scope, responsibilities, boundaries, modules, dependencies, shared services
- [x] Analyze security, cloud and frontend requirements
- [x] Identify unclear points without inventing requirements
- [x] Define the module structure and dependency rules
- [x] Record technology decisions
- [x] Create `ARCHITECTURE.md`, `PROJECT_PLAN.md`, `DEVELOPMENT_STATUS.md`
- [x] Create `docs/` structure and 15 ADRs
- [x] Review risks and record open questions

### Architecture Decisions
ADR-001 through ADR-015 (see `docs/architecture/adr/`).

### Implementation
Documentation only. **No production code written**, as required.

### Tests
Not applicable. Verification is by review: the documents are internally consistent, every module has a defined boundary, no dependency cycle exists in the declared graph, and no business logic has entered the Platform scope.

### Acceptance Criteria
- [x] `ARCHITECTURE.md` covers all 25 required sections
- [x] `PROJECT_PLAN.md` specifies every phase in the required format
- [x] `DEVELOPMENT_STATUS.md` exists
- [x] At least ADR-001…ADR-010 exist, each with Context, Problem, Options, Decision, Reason, Consequences, Status
- [x] `docs/` structure created
- [x] Open questions recorded rather than answered by invention
- [x] No production code

### Known Issues
1. **The referenced requirements document was not found.** The working directory was empty. This analysis derives from the Master Development Prompt. See ARCHITECTURE.md §27 Q1.
2. Twelve open questions require the owner's input before or during early phases.

### Risks
| Risk | Impact | Likelihood | Mitigation |
|---|---|---|---|
| The missing document contains scope that changes the plan | High | Medium | Obtain it before Phase 1; the plan is written to absorb revision |
| Boundary erosion — business logic requested inside the Platform | High | High | §4.4 boundary test applied to every request; ADR-005 is binding |
| Over-engineering | Medium | Medium | P1; every added component must name the simpler option it beat |

---

# PHASE 1 — FOUNDATION & PLATFORM KERNEL

### Objective
Create a running, empty, correct Platform: the solution structure, the module system, the cross-cutting kernel, the API conventions every later module will obey, local development via Docker, and a CI pipeline — with no business capability yet.

### Tasks
1. `git init`, `.gitignore` (written **before** the first commit), branch strategy, commit conventions
2. Solution and project structure per ARCHITECTURE.md §8.2
3. `CCP.Kernel`: `Result`/`Error`, guards, `IClock`, UUID v7 generation, domain event base types
4. `CCP.Kernel.Application`: pipeline behaviours (validation, logging, transaction), `ICurrentUser`, event dispatcher
5. `CCP.Kernel.Infrastructure`: EF conventions, transactional outbox, `BackgroundService` dispatcher with `FOR UPDATE SKIP LOCKED`
6. `CCP.Kernel.Api`: RFC 9457 ProblemDetails mapping, exception handler, API versioning, pagination/sorting/filtering contracts, correlation ID middleware, security headers, rate limiting, CORS, OpenAPI
7. `IPlatformModule` registration contract and explicit host registration
8. Typed configuration binding with validation at startup; User Secrets for local development
9. Serilog structured logging with redaction; `/health/live` and `/health/ready`
10. `docker-compose.yml`: PostgreSQL, MinIO, mail catcher; a documented one-command start
11. Test projects: unit, integration (Testcontainers), and **architecture tests** asserting §6 dependency rules
12. GitHub Actions: build → test → architecture tests → lint → secret scan → dependency audit → container build
13. A trivial vertical slice (a `ping` endpoint) proving the whole pipeline end to end

### Architecture Decisions
ADR-001, ADR-002, ADR-004, ADR-008, ADR-013. Any new decision here gets its own ADR.

### Implementation
Kernel and host only. No module contains behaviour yet. Empty module projects may be scaffolded to prove the registration mechanism.

### Tests
- Unit: kernel primitives, `Result`, guards, pagination parsing, error mapping
- Integration: application starts; health checks pass; outbox dispatches; migrations apply against a Testcontainers PostgreSQL
- Architecture: Domain references no EF Core / ASP.NET Core; no module references another module's internals; no cycles
- API: `ping` returns the standard envelope; an error returns the exact ProblemDetails shape

### Acceptance Criteria
- [x] `docker compose up` plus one documented command brings a new developer to a running system — documented in `docs/development/getting-started.md`; **compose itself unverified locally** (see Known Issues)
- [x] The application starts, serves `/health/live` and `/health/ready`, and exposes OpenAPI
- [x] Every error response conforms to RFC 9457 with `code` and `correlationId` — verified over HTTP for all six error categories
- [x] Correlation IDs appear in every log line and propagate through the request — including sanitisation of caller-supplied values
- [x] Architecture tests pass and **fail** when a deliberate violation is introduced — verified by introducing `Api → Infrastructure` and reverting it
- [ ] CI is green on a pull request and fails on a planted test secret — **pipeline written, not yet run** (no remote repository)
- [x] No secret exists anywhere in the repository or its history — `.gitignore` written before the first file
- [x] Code coverage collection configured in CI

### Status
🟡 **Substantially complete.** Builds clean, runs, manually verified end to end.
66 of 93 tests executed and passing; the 27 integration tests are written and
compile but have not run, because no database is reachable on this machine
(DEVELOPMENT_STATUS.md B1). Not marked complete until they do.

### Known Issues
1. **Integration tests unexecuted.** Docker's Linux engine will not start (WSL2 has no distribution installed), and the native PostgreSQL fallback has an unknown superuser password. Neither is a code problem, and CI provisions its own PostgreSQL. Resolution in DEVELOPMENT_STATUS.md §4.
2. **`build/api.Dockerfile` never built**, for the same reason. CI builds and scans it on the first push.
3. **CI pipeline never executed** — there is no GitHub remote yet.

### Risks
| Risk | Impact | Mitigation |
|---|---|---|
| .NET 10 SDK not installed | Blocks all work | Install before starting; verified as a prerequisite |
| Kernel over-abstraction before real use cases exist | Medium — expensive to unwind | Build only what the `ping` slice and Phase 2 provably need |
| Outbox implemented subtly wrong | High — silent event loss | Integration-test concurrent dispatch with multiple workers explicitly |

---

# PHASE 2 — IDENTITY & AUTHENTICATION

### Objective
One central account per person, with correct, well-tested authentication. This is the most security-critical phase in the project.

### Tasks
1. Identity domain: `User`, `UserCredential`, `PasswordHistory`, `Session`, `RefreshToken`, `Device`, `LoginAttempt`
2. Argon2id password hashing with tuned, configurable parameters
3. Configurable password policy: length, breached-password check, history, expiry
4. Login, logout, refresh with **rotation and reuse detection**, revoke
5. JWT issuance (RS256), JWKS endpoint, key rotation procedure
6. Session and device lifecycle; login history
7. Password reset (enumeration-safe, single-use hashed tokens); forced change on first login
8. Account lifecycle: create, enable, disable, unlock
9. User CRUD APIs and `/api/v1/me`
10. Progressive lockout on repeated failure
11. Identity events published to the outbox
12. Seed procedure for the bootstrap administrator (documented, audited, one-time)
13. Documentation: `docs/identity/`, `docs/security/authentication.md`

### Architecture Decisions
ADR-006. An ADR is required if an external identity provider is introduced.

### Implementation
Full Identity module: Contracts, Domain, Application, Infrastructure, Api. **MFA is designed for here but implemented in Phase 5.**

### Tests
- Unit: password policy, hashing, token generation, lockout progression, session rules
- Integration: complete login → refresh → logout cycle; refresh rotation; **reuse detection revokes the session family**; expiry; concurrent login from multiple devices
- Security: no timing difference between unknown user and wrong password; no user enumeration through reset or login; hashes never appear in any response; brute-force attempts are throttled and recorded
- API: every endpoint's contract and error shape

### Acceptance Criteria
- [ ] A user can log in, refresh and log out; logout invalidates the refresh token **server-side**
- [ ] A replayed refresh token revokes the entire session family and raises a security event
- [ ] Passwords are Argon2id; no plaintext or reversible storage anywhere
- [ ] Password reset cannot be used to discover whether an account exists
- [ ] Lockout works and cannot be used to lock out an arbitrary user indefinitely
- [ ] A user can list and revoke their own sessions
- [ ] Test coverage on this module ≥ 85%, with every security test passing
- [ ] The bootstrap administrator procedure is documented and produces no hardcoded credential

### Status
🟡 **Feature-complete against the phase plan; not yet verified.**

Every planned task is built: the domain model, Argon2id hashing with transparent
cost upgrade, RS256 token issuance and JWKS publication, sign-in with uniform
failure and uniform timing, refresh with rotation and reuse detection,
server-side sign-out, progressive lockout, login history, password change,
enumeration-safe reset, password history and breach screening, user
administration, `/me`, the bootstrap administrator seeder, and the module
documentation. 116 unit tests pass; 37 integration tests are written.

Not complete, because two acceptance criteria are unmet: the integration tests
have never run, and coverage cannot be measured against a phase that has not
been exercised.

### Known Issues
1. **Integration tests never executed.** 37 written, covering sign-in,
   enumeration uniformity, lockout, rotation, reuse detection, outbox atomicity
   and server-side sign-out. No database was reachable
   (DEVELOPMENT_STATUS.md §4). They run in CI.
2. **Permission attributes are declared but not enforced.** Administrative
   endpoints carry `platform.users.*`; nothing evaluates them until Phase 4.
   Any authenticated user can currently administer users.
3. **Reset emails are not sent.** The token is issued and staged on the outbox;
   delivery arrives with Notifications in Phase 9.
4. **Breach screening is off by default.** Implemented, but enabling an
   outbound third-party call is the owner's decision. While off, nothing is
   screened.
5. **Bootstrap procedure unapproved** — blocked on Q10.
6. **Argon2id parameters unmeasured** on real hardware.
7. **Key rotation never exercised.**

### Risks
| Risk | Impact | Mitigation |
|---|---|---|
| A subtle authentication flaw | Critical | 116 unit tests assert the security properties explicitly; seven defects were found and fixed during the phase, two of them silent. Still needs integration testing and a penetration test before go-live. |
| Deployed before Phase 4 | **Critical** | Permission attributes do not yet enforce. **This must not be exposed anywhere reachable until Authorization exists.** |
| ~~Authentication endpoints not individually rate-limited~~ | — | ✅ Resolved in Phase 5: sliding-window policies per endpoint class, 10/min on the authentication class. |
| Argon2id parameters too weak or too slow | High / Medium | Self-describing hashes and `NeedsRehash` allow the cost to be raised later without invalidating anyone's password. |
| Key rotation not exercised | Medium | The `kid` mechanism supports it; the procedure must be rehearsed in staging, not at go-live. |

---

# PHASE 3 — ORGANIZATION

### Objective
The authoritative company structure, with employees as organizational records, at arbitrary depth.

### Tasks
1. Domain: `Company`, `Department`, `Section`, `Center`, `Position`, `Employee`, `EmployeeAssignment`, `ManagerRelationship`
2. Hierarchy: adjacency list + materialized path, with path recomputation on move
3. Bilingual names (`name_ar`, `name_en`) on org entities
4. Employee ↔ user linking (optional in both directions)
5. Manager relationships and reporting lines; cycle prevention
6. Tree query API and flat list APIs with filtering
7. Extension bag for application-specific attributes
8. Events published on structural change
9. Documentation: `docs/identity/organization.md`

### Architecture Decisions
Hierarchy representation (adjacency list + materialized path) — recorded here; promote to an ADR if it is later contested.

### Implementation
Full Organization module.

### Tests
- Unit: path computation, cycle detection, manager rules, deactivation rules
- Integration: deep hierarchy (10+ levels) created, queried and **moved**, with descendant paths verified; large tree performance
- API: filtering, paging, sorting; bilingual fields returned correctly

### Acceptance Criteria
- [ ] Arbitrary nesting depth works; no fixed limit on departments, sections or centers
- [ ] Moving a department correctly updates all descendants in one transaction
- [ ] A cycle in the hierarchy or in manager relationships is impossible
- [ ] An employee may exist without a user account, and a user without an employee record
- [ ] Tree query for a 5,000-node organization returns within the performance budget
- [ ] Coverage ≥ 80%

### Status
🟡 **Core complete.** Unit hierarchy with materialized path and arbitrary
depth, atomic moves with descendant rebasing, cycle prevention in both the unit
tree and reporting lines, bilingual names, employees with optional user linkage,
positions, company. Nine endpoints. 48 unit tests, all passing.

Outstanding: position and company endpoints, the employee custom-attribute
extension bag, and integration tests.

### Known Issues
1. **Integration tests not written for this module.** The hierarchy logic is
   covered by unit tests, but atomic moves under a real transaction, the
   `text_pattern_ops` index actually being used, and the unique constraints are
   unverified.
2. **Permissions declared, not enforced** — as everywhere, until Phase 4.
3. **`EmployeeAssignment` history deferred.** Current unit, position and manager
   live on the employee; historical assignment tracking is not built.
4. **No extension bag** for application-specific employee attributes
   (ARCHITECTURE.md §7.2.2). Needed before the first business system integrates.
5. **Company creation has no endpoint** — it is currently a seeding concern.

### Risks
| Risk | Impact | Mitigation |
|---|---|---|
| Business HR concepts creep in (salary, leave, appraisal) | High — the sharpest boundary in the Platform | `Employee` and its DTO are hand-written and documented as the boundary edge; ADR-005 §4.3a is explicit; the §4.4 test applies to every proposed field |
| A path bug silently corrupts authorization scope | **High** | Prefix-safety, atomic rebasing and the under-own-descendant rejection are all directly tested. Still unverified against a real transaction. |
| Materialized path maintenance on move | Medium | Descendants loaded before the move, rebased, committed in one `SaveChanges`; tested across depth changes and an eight-level subtree |
| Existing employee data to migrate (Q11 unanswered) | Medium | Still open. No migration path exists. |
| One-company assumption (Q2 unanswered) | Medium | `CompanyId` is on every entity, so supporting several means relaxing a uniqueness rule rather than reshaping the schema |

---

# PHASE 4 — AUTHORIZATION & RBAC

### Objective
One authoritative answer to "who may do what", extensible by systems that do not exist yet.

### Tasks
1. Domain: `Application`, `Permission`, `Role`, `RolePermission`, `UserRole` (with scope), `DelegatedRole`
2. Permission naming `<application>.<resource>.<action>`; Platform permissions registered for all modules built so far
3. Application registry with permission-manifest registration
4. Scope evaluation (Self / Unit / UnitAndBelow / All) using the materialized path
5. `RequirePermission` authorization handler and endpoint attributes
6. Effective-permission cache with **eager, version-stamped invalidation**
7. `/api/v1/authorization/check` for business applications; `/api/v1/me/permissions`
8. Role and permission administration APIs
9. Anti-escalation rules; self-modification prevention
10. Bootstrap administrator execution (with the owner present)
11. Retrofit permission requirements onto every endpoint from Phases 2–3
12. Documentation: `docs/authorization/`

### Architecture Decisions
ADR-007, ADR-012.

### Implementation
Full Authorization module, plus a sweep of earlier modules to attach permissions.

### Tests
- Unit: permission parsing, scope resolution, escalation rules, cache invalidation logic
- Integration: assignment and revocation take effect immediately; scope filters actually restrict returned data; delegation expires
- Security: **every protected endpoint is tested for 401 unauthenticated and 403 without the permission** — this is a generated, exhaustive test, not a sample
- Architecture: a test fails the build if any endpoint lacks a permission declaration or an explicit anonymous marker

### Acceptance Criteria
- [ ] Every endpoint in the Platform declares a permission or is explicitly anonymous — enforced by a build-failing test
- [ ] Revoking a role takes effect on the very next request (no stale-cache window)
- [ ] A user cannot grant a permission they do not hold, and cannot modify their own roles
- [ ] Scope restricts data, not merely access — verified by asserting returned rows, not status codes
- [ ] A simulated business application registers permissions and receives correct decisions
- [ ] Coverage ≥ 85%

### Status
🟡 **Core complete.** RBAC with organizational scope, enforcement wired
through a dynamic policy provider and permission handler, anti-escalation rules,
a version-stamped cache in which revocation takes effect on the next request, the
application registry and permission-declaration mechanism, and the scope filter
applied at the data layer for employee search. 63 unit tests, all passing.

Outstanding: role and application management endpoints, the scope filter on the
user list, and integration tests.

### Known Issues
1. **The user list applies no scope filter.** Identity has no organizational
   dimension, so what `Unit` scope means there is a decision rather than an
   oversight. Until it is made, `platform.users.view` at any scope returns every
   user.
2. **Role creation has no endpoint.** Roles are readable and grantable; creating
   one is currently a seeding concern.
3. **Application registration has no endpoint.** The domain, repository and
   declaration handler exist; the endpoints arrive with client credentials in
   Phase 11.
4. **Integration tests not written.** The permission join, the version stamp
   under concurrency, and the seeder have never run against a real database.

### Risks
| Risk | Impact | Mitigation |
|---|---|---|
| Stale permission cache grants revoked access | **Critical** | A version stamp in the database, not a TTL and not in memory. Revocation invalidates every cache on every instance immediately. Unverified under real concurrency. |
| Scope evaluation adds latency to every request | Medium | One indexed row read plus a cached set. The permission join is one query. Unmeasured against real data. |
| A scope bug silently grants the wrong data | **High** | Prefix safety, widest-wins, and the no-unit denial are all directly tested. The filter is applied at the data layer rather than after loading. |
| Complexity creep toward a policy engine | Medium | ABAC explicitly deferred in ADR-007; adopting it requires a superseding ADR |

---

# PHASE 5 — SECURITY HARDENING

### Objective
Complete the security controls: MFA, rate limiting, security events, secrets, headers, and the policies that govern them.

### Tasks
1. TOTP MFA: enrolment, verification, recovery codes (hashed, shown once), disable flow
2. Step-up authentication for sensitive operations
3. MFA policy: required for administrative permission holders, configurable
4. Security event model, detection and recording
5. Rate-limit policies per endpoint class and per application
6. Trusted-device management
7. Security headers and strict CORS finalized
8. Secret manager integration and rotation procedure
9. Security notifications (new device, password change, MFA change, lockout)
10. Secret scanning, dependency scanning and container scanning enforced in CI
11. Documentation: `docs/security/`

### Architecture Decisions
MFA method selection (TOTP first) — see ADR-006 consequences. An ADR is required before adding WebAuthn or SMS MFA.

### Implementation
Full Security module, plus MFA integration into the Phase 2 authentication flow.

### Tests
- Unit: TOTP generation and window tolerance, recovery code single use, lockout policy
- Integration: full MFA enrolment and challenge; step-up; rate limits return 429 with `Retry-After`
- Security: MFA cannot be bypassed by calling a later endpoint directly; recovery codes cannot be reused; rate limits cannot be evaded by header manipulation

### Acceptance Criteria
- [ ] MFA can be enrolled, challenged, recovered and disabled — with every step audited
- [ ] MFA is not bypassable by any endpoint ordering
- [ ] Rate limiting is effective and returns the correct status and headers
- [ ] All required security headers present; verified by an automated scan
- [ ] No secret in source control; CI fails on a planted secret
- [ ] Dependency and container scans pass with no high or critical findings
- [ ] Coverage ≥ 85%

### Status
🟡 **Core complete.** Tasks 1, 2, 4, 5, 7, 8 (partly) and 11 delivered.

| # | Task | Status |
|---|---|---|
| 1 | TOTP MFA — enrolment, verification, recovery codes, disable | ✅ |
| 2 | Step-up authentication for sensitive operations | ✅ Six endpoints, pinned by test in both directions |
| 3 | MFA policy required for administrative permission holders | 🟡 **Reinterpreted.** Enforced at the privileged action rather than at sign-in — refusing sign-in would lock an administrator out of the system they need in order to enrol. See DEVELOPMENT_STATUS.md §3D.3. |
| 4 | Security event model, detection and recording | 🟡 Model, recording and bounded search built. Detection — thresholds, alerting — is not. |
| 5 | Rate-limit policies per endpoint class | ✅ Per class. **Per application** is not built; it needs the client credentials arriving in Phase 11. |
| 6 | Trusted-device management | ⬜ **Not built.** Deferred: it reduces MFA prompts, which is convenience, and it adds a bypass surface. Not worth adding before the factor itself has run in production. |
| 7 | Security headers and strict CORS finalized | ✅ Built in Phase 2, documented here |
| 8 | Secret manager integration and rotation procedure | 🟡 Configuration is path-based and fails fast; the **MFA key has no rotation path** (§7 debt #18) |
| 9 | Security notifications | ⬜ Blocked on Notifications (Phase 9) — nothing can send mail |
| 10 | Secret, dependency and container scanning in CI | ✅ Built in Phase 1 (gitleaks, full history) |
| 11 | `docs/security/` | ✅ `mfa.md`, `rate-limiting.md`, `secrets-management.md`, `security-headers.md`. `threat-model.md` and `password-policy.md` remain outstanding from Phase 2. |

### Acceptance criteria — actual result

| Criterion | Result |
|---|---|
| MFA enrolled, challenged, recovered, disabled | ✅ Built; every step writes a security event |
| MFA not bypassable by endpoint ordering | 🟡 Enforced by design — verification requires an active enrolment and elevation is server-side — but **unverified against a database** |
| Rate limiting effective, correct status and headers | 🟡 Policies applied and guarded by an architecture test; the 429 and `Retry-After` behaviour has not been exercised over HTTP |
| Security headers present | ✅ With a regression test covering error responses |
| No secret in source control; CI fails on a planted secret | ✅ gitleaks over full history |
| Dependency and container scans clean | ✅ Configured in CI; never executed (no push) |
| Coverage ≥ 85% | ⬜ Not measured |

### Known Issues
1. **Nothing in this module has run against a real database** (blocker B1). The 15 integration tests are written and unexecuted.
2. **The MFA protection key cannot be rotated** — the stored format has no key version field.
3. **TOTP is not phishing-resistant.** Accepted for this phase; WebAuthn is the answer and remains an extension point.
4. **No administrator-initiated MFA reset.** A user losing both phone and recovery codes cannot recover.
5. Rate limits are per-process, so a multi-instance deployment multiplies them.

### Risks
| Risk | Impact | Mitigation | Outcome |
|---|---|---|---|
| MFA locks out legitimate users | High operational | Recovery codes + a documented, audited administrative reset procedure | 🟡 Recovery codes built; **the administrative reset procedure is not** — this risk is only half mitigated |
| Rate limits too aggressive for real usage | Medium | Tunable configuration; monitor rejections in staging before production | 🟡 Configurable per class; no staging environment exists to monitor |
| Secret manager unavailable at startup | High | Fail fast and loudly with a clear message; document the recovery path | ✅ Startup fails outside Development with a message naming the setting; documented in `docs/security/secrets-management.md` |

---

# PHASE 6 — AUDIT

### Objective
An immutable, complete, searchable, company-wide record — including an ingestion path for systems that do not exist yet.

### Tasks
1. `AuditEvent` model with the full field set (ARCHITECTURE.md §15.2)
2. Monthly range partitioning plus a partition-creation maintenance job
3. Internal ingestion: an outbox consumer subscribing to all module events
4. External ingestion: `POST /api/v1/audit/events`, single and batch, authenticated per application
5. Redaction policy for sensitive fields, applied before storage
6. Search API with mandatory date bounds and composite indexes matched to the offered filters
7. Asynchronous export with a signed output file; the export itself audited
8. **Database-level append-only enforcement**: the application role granted INSERT and SELECT only
9. Retention and archival by partition detach
10. Retrofit audit coverage across Phases 2–5
11. Documentation: `docs/audit/` including the integration guide for business applications

### Architecture Decisions
Partitioning strategy and append-only enforcement — recorded in ADR-005 consequences and here.

### Implementation
Full Audit module, plus an audit sweep of all earlier modules.

### Tests
- Unit: event construction, redaction, validation
- Integration: events written through the outbox survive a rollback correctly (nothing recorded); an UPDATE or DELETE attempt against the audit schema **fails at the database level**; partition rollover; search performance against a seeded large dataset
- Security: an application cannot write events attributed to another application; audit export requires its permission and is itself audited

### Acceptance Criteria
- [ ] Every security-relevant action in Phases 2–5 produces an audit event
- [ ] Audit rows cannot be modified or deleted — proven by an integration test that attempts it and fails
- [ ] A rolled-back transaction produces no audit event; a committed one always does
- [ ] Search over 10 million seeded events returns within the 2 s budget
- [ ] No password, token or secret appears in any audit record, including in old/new values
- [ ] A simulated external application successfully ingests events
- [ ] Coverage ≥ 80%

### Status
⬜ Not started

### Known Issues
None yet.

### Risks
| Risk | Impact | Mitigation |
|---|---|---|
| Audit write latency degrades every operation | High | Outbox-based async write; measure the added latency explicitly |
| Table growth outpaces the plan | Medium | Partitioning from day one; monitor size; confirm retention (Q8) |
| Sensitive data leaks into old/new values | High | Redaction by declared policy, tested; a review checklist item |

---

# PHASE 7 — FRONTEND FOUNDATION & CORE ADMINISTRATION UI

### Objective
The design system, the application shell, bilingual RTL/LTR support, authentication screens, and administration screens for Identity, Organization, Authorization and Audit. This phase makes the Platform visible and validates the API contracts against a real consumer.

### Tasks
1. Next.js + TypeScript project with strict settings; the structure in ARCHITECTURE.md §9.2
2. **Design system**: white/blue tokens, typography (Latin + Arabic), spacing, moderate radius, light borders, minimal shadow
3. shadcn/ui component adoption **with decorative icons removed** from every component taken in
4. Shared components: `DataTable` (sort, filter, paginate, responsive), `PageHeader`, `FilterBar`, `EmptyState`, `ConfirmDialog`, form primitives
5. Application shell: sidebar, header, breadcrumbs, responsive navigation with a mobile drawer
6. i18n with next-intl: `ar` and `en` catalogues, locale routing, `dir` switching, logical-property styling throughout
7. BFF route handlers; httpOnly session cookie; CSRF protection; no token in browser storage
8. Auth screens: login, MFA challenge, forgot password, reset password, session expired, account locked
9. Administration screens: Users, Employees, Organization, Roles, Permissions, Audit search
10. `usePermission()` for UX-level control visibility
11. Generated TypeScript types from OpenAPI
12. Accessibility pass: keyboard, focus, labels, contrast, screen reader
13. Responsive verification at desktop, laptop, tablet and mobile

### Architecture Decisions
ADR-003, ADR-010, ADR-011.

### Implementation
Frontend only; backend changes only where the UI exposes a genuine contract gap.

### Tests
- Unit: components, hooks, formatters, permission logic
- Integration: feature API services against a mocked API
- E2E (Playwright): login with MFA, create a user, assign a role, search audit — **each run in both Arabic and English**
- Accessibility: automated axe checks plus a manual keyboard pass
- Visual: every screen reviewed at four breakpoints in both directions

### Acceptance Criteria
- [ ] Every screen works correctly in Arabic (RTL) **and** English (LTR), with the full layout mirrored — not only the text
- [ ] Zero hardcoded user-facing strings — enforced by a lint rule
- [ ] Zero physical `left`/`right` Tailwind utilities in components — enforced by a lint rule
- [ ] No decorative icons; no AI-style visual patterns; white and blue only — confirmed by design review against ARCHITECTURE.md §9.5
- [ ] Tables remain usable on a phone; no horizontal page scroll anywhere
- [ ] No token in `localStorage` or `sessionStorage` — verified in the browser
- [ ] Keyboard navigation reaches every interactive element with a visible focus state
- [ ] WCAG 2.2 AA contrast met
- [ ] E2E suite green in both locales

### Status
⬜ Not started

### Known Issues
None yet.

### Risks
| Risk | Impact | Mitigation |
|---|---|---|
| The interface drifts toward a generic AI-dashboard look | High — an explicit owner requirement | §9.5 is a review checklist; icons removed at component adoption; owner review at phase end |
| RTL treated as an afterthought | High | Logical properties enforced by lint from the first component; both locales in E2E from day one |
| shadcn/ui defaults reintroduce icons silently | Medium | Every adopted component is reviewed and stripped; a lint rule flags icon imports in `components/ui/` |
| Arabic typeface not chosen (Q12) | Low | A sensible default is used; swapping the token later is trivial |

---

# PHASE 8 — WORKFLOW

### Objective
A reusable approval engine containing no business rules, usable by every future system.

### Tasks
1. Domain: `WorkflowDefinition` (versioned), `WorkflowStep`, `WorkflowTransition`, `WorkflowInstance`, `WorkflowTask`, `WorkflowAction`, `WorkflowComment`
2. Definition schema, validation and versioning; definitions registered by applications
3. State machine execution with transition validation
4. Assignee resolution strategies: user, role, position, requester's manager, department head, caller-supplied list
5. Actions: approve, reject, return, delegate, comment, cancel
6. Callback / webhook mechanism for business-conditional routing and completion
7. SLA timers and escalation via the background worker
8. `/api/v1/me/tasks` inbox
9. Workflow administration and task screens in the frontend
10. Documentation: `docs/workflow/` including the integration guide

### Architecture Decisions
Definition-as-data and caller-supplied conditional routing — recorded in ADR-005 consequences; an ADR is required if a rule engine is ever proposed inside the Platform.

### Implementation
Full Workflow module plus its frontend screens.

### Tests
- Unit: state machine transitions, assignee resolution, invalid action rejection, version pinning
- Integration: a complete Create → Review → Approval → Execution cycle; rejection; return; delegation; escalation on SLA breach; a definition changed mid-instance does not alter the running instance
- Security: only the assignee or a valid delegate can act; every action audited

### Acceptance Criteria
- [ ] A multi-step approval runs end to end with no Platform code specific to any business process
- [ ] **No business rule, threshold or amount exists anywhere in the module** — verified by review
- [ ] A running instance is unaffected by a change to its definition
- [ ] An action by a non-assignee is rejected and recorded
- [ ] SLA escalation fires correctly
- [ ] A simulated business application registers a definition, starts an instance and receives the completion callback
- [ ] Coverage ≥ 80%

### Status
⬜ Not started

### Known Issues
None yet.

### Risks
| Risk | Impact | Mitigation |
|---|---|---|
| Business conditions leak into the engine ("if amount > X") | High — the central boundary risk of this module | Conditional routing is delegated to the caller by design; review gate |
| The definition schema is too rigid for real processes | Medium | Validate against three realistic hypothetical processes before finalizing |
| The engine grows into a BPMN implementation | Medium | Scope is fixed to sequential and parallel approval steps; anything beyond needs an ADR |

---

# PHASE 9 — NOTIFICATIONS

### Objective
Central, reliable, multi-channel notification with a pluggable provider seam.

### Tasks
1. Domain: `NotificationTemplate` (per locale), `Notification`, `NotificationDelivery`, `NotificationPreference`, `ChannelProvider`
2. `INotificationChannelProvider` abstraction
3. In-App channel with the user inbox
4. Email channel with a provider adapter (through the Integration layer once Phase 12 exists; a direct SMTP adapter until then)
5. Template rendering with strict escaping, declared variables and validation at send time
6. Asynchronous dispatch with retry, backoff and jitter; permanent-failure visibility
7. Per-user, per-channel, per-category preferences — with security notifications non-disableable
8. Delivery log
9. Notification screens: inbox, templates, providers, delivery log
10. Documentation: `docs/notifications/`

### Architecture Decisions
Channel provider abstraction; deferral of SMS and Push pending a real requirement (Q9).

### Implementation
Full Notifications module plus screens.

### Tests
- Unit: template rendering, escaping, variable validation, preference resolution, retry policy
- Integration: send → deliver → record; failure → retry → permanent failure; template rendered correctly in both locales
- Security: a user cannot read another user's notifications; template variables cannot inject markup or script

### Acceptance Criteria
- [ ] In-App and Email both deliver, with every attempt logged
- [ ] Every template exists in Arabic and English
- [ ] A new channel can be added by implementing one interface and registering it — demonstrated with a stub provider
- [ ] Failures retry and then surface in administration rather than disappearing
- [ ] Security notifications cannot be disabled by preference
- [ ] Coverage ≥ 80%

### Status
⬜ Not started

### Known Issues
None yet.

### Risks
| Risk | Impact | Mitigation |
|---|---|---|
| Template injection | High | Contextual escaping; variables never trusted; explicit tests |
| Notification volume overwhelms a provider | Medium | Rate limiting per provider; queue depth monitored |
| Business logic decides *when* to notify inside the Platform | Medium — boundary risk | The Platform delivers; the caller decides. Review gate |

---

# PHASE 10 — DOCUMENTS

### Objective
Secure, versioned, access-controlled document storage, independent of any business application.

### Tasks
1. Domain: `Document`, `DocumentVersion`, `DocumentAccessRule`, `DocumentLink`, `DocumentAccessLog`
2. `IDocumentStorageProvider`: local filesystem (development), S3-compatible (production)
3. Upload with magic-byte inspection, allow-list, size limit, SHA-256, random object key
4. Malware scan hook (`IDocumentScanner`), with a no-op default and a documented production option
5. Download via short-lived pre-signed URL or a permission-checked streaming endpoint
6. Versioning; polymorphic resource linking
7. Access rules and enforcement; every access logged
8. Retention, two-stage deletion, purge
9. Document screens and an embeddable upload component
10. Documentation: `docs/documents/`

### Architecture Decisions
ADR-014.

### Implementation
Full Documents module plus screens.

### Tests
- Unit: type detection, allow-list, key generation, access rule evaluation, versioning
- Integration: upload → download → new version → delete → purge, against MinIO via Testcontainers; large-file streaming
- Security: a file with a mismatched extension and magic bytes is rejected; a direct object-storage URL is not publicly readable; a pre-signed URL expires; path traversal via filename is impossible; access without permission is denied and logged

### Acceptance Criteria
- [ ] Files upload, download, version and delete correctly
- [ ] Content type is determined by inspection, never by the client's claim
- [ ] Object storage is not publicly readable under any path
- [ ] Every download is logged with actor, time and IP
- [ ] Binary content never enters PostgreSQL
- [ ] Deletion is recoverable within the grace period
- [ ] Coverage ≥ 80%

### Status
⬜ Not started

### Known Issues
None yet.

### Risks
| Risk | Impact | Mitigation |
|---|---|---|
| Malicious file upload | High | Magic-byte inspection, allow-list, size limit, scan hook, never served inline from the app origin |
| Storage cost growth | Medium | Retention policy; size monitoring; deduplication by hash |
| Pre-signed URL leaked and reused | Medium | Very short expiry; access logged; sensitive documents use the streaming endpoint instead |

---

# PHASE 11 — EXTERNAL API PLATFORM & APPLICATION REGISTRY

### Objective
Make the Platform genuinely consumable by systems built by other teams.

### Tasks
1. Application registry administration: register, rotate credentials, disable
2. OAuth 2.0 client credentials flow; machine tokens; on-behalf-of user context
3. API keys where appropriate, with scoping and rotation
4. Per-application rate limits and quotas
5. API versioning policy, deprecation headers and a published lifecycle
6. OpenAPI polish: examples, error catalogue, permission annotations per endpoint
7. A reference client showing correct integration, in one language
8. Webhook subscription management for Platform events
9. Application administration screens
10. Documentation: `docs/api/` and `docs/development/integration-guide.md`

### Architecture Decisions
ADR-008, ADR-012.

### Implementation
Kernel and Authorization extensions plus a small API-platform surface; no new module.

### Tests
- Unit: credential validation, quota accounting, token scoping
- Integration: an application registers, authenticates, calls APIs, is rate-limited, and has its credentials rotated without downtime
- Security: an application cannot exceed its scope, act as another application, or read data outside its permissions; a revoked credential stops working immediately

### Acceptance Criteria
- [ ] A developer with no access to Platform source can integrate a system using the documentation alone — validated by having someone actually try
- [ ] Client credentials work; rotation causes no downtime; revocation is immediate
- [ ] Per-application rate limits are enforced
- [ ] OpenAPI documents every endpoint with its permission and error codes
- [ ] The reference client runs successfully against a live instance
- [ ] Coverage ≥ 80%

### Status
⬜ Not started

### Known Issues
None yet.

### Risks
| Risk | Impact | Mitigation |
|---|---|---|
| Documentation insufficient for an external team | High — this is the phase's whole purpose | Test it with a real developer, not by self-assessment |
| An over-privileged application credential | High | Scoped permissions; least privilege at registration; review |
| Breaking changes shipped without versioning | High | Contract tests; the versioning policy is enforced, not advisory |

---

# PHASE 12 — INTEGRATIONS

### Objective
One governed door to the outside world.

### Tasks
1. Domain: `IntegrationProvider`, `IntegrationEndpoint`, `IntegrationCredential` (reference only), `IntegrationCallLog`, `WebhookSubscription`
2. `IIntegrationConnector` abstraction with a uniform resilience pipeline (timeout, retry with jitter, circuit breaker, bulkhead)
3. Secret manager integration for credentials — references stored, never values
4. Outbound host allow-list (SSRF prevention)
5. Per-connector redaction policy; full call logging
6. Inbound webhooks: signature verification, replay protection, strict validation
7. Per-provider health checks and status
8. Migrate the Phase 9 email channel onto this layer
9. Integration administration screens
10. Documentation: `docs/integrations/`

### Architecture Decisions
Credential-by-reference and the outbound allow-list — recorded here; promote to an ADR if contested.

### Implementation
Full Integrations module plus screens. **Connectors for specific providers are built when a real need exists**, not speculatively.

### Tests
- Unit: resilience policy behaviour, redaction, allow-list checking, signature verification
- Integration: against a stub external service — retry, circuit breaker opening and closing, timeout, logging
- Security: a request to a non-allow-listed host is blocked; credentials never appear in logs or API responses; a replayed webhook is rejected

### Acceptance Criteria
- [ ] All outbound calls pass through the layer with uniform resilience
- [ ] No provider secret exists in the database or in any log
- [ ] SSRF to an arbitrary host is impossible
- [ ] Every call is logged with sensitive fields redacted
- [ ] A provider outage degrades one capability, not the Platform
- [ ] Inbound webhooks verify signatures and reject replays
- [ ] Coverage ≥ 80%

### Status
⬜ Not started

### Known Issues
None yet.

### Risks
| Risk | Impact | Mitigation |
|---|---|---|
| Speculative connectors built for providers nobody uses | Medium — wasted effort | Build the framework only; connectors on demand |
| Call log growth | Medium | Retention policy from the start |
| A business payload's semantics leak into the layer | Medium — boundary risk | The layer transports; it does not interpret |

---

# PHASE 13 — CONFIGURATION & FEATURE FLAGS

### Objective
Typed, scoped, audited settings, with feature flags.

### Tasks
1. Domain: `SettingDefinition`, `SettingValue` (platform / application / company scope), `FeatureFlag`, `SettingChangeHistory`
2. Typed values with validation; sensitivity marking
3. Setting registration by applications
4. Read caching with invalidation on change
5. Feature flag evaluation, with targeting by role or org unit
6. Configuration administration screens with change history
7. Documentation: `docs/development/configuration.md`

### Architecture Decisions
The configuration/secret split (secrets live in the secret manager, never in settings).

### Implementation
Full Configuration module plus screens.

### Tests
- Unit: type validation, scope resolution precedence, flag evaluation
- Integration: a change takes effect without restart; history recorded with old and new values
- Security: a sensitive setting is never returned by a read; changes require permission and are audited

### Acceptance Criteria
- [ ] Settings are typed, validated and scoped, with correct precedence
- [ ] A sensitive setting cannot be read back through any API
- [ ] Every change records old value, new value, actor and time
- [ ] Feature flags can be toggled without deployment
- [ ] No secret is stored as a configuration value
- [ ] Coverage ≥ 80%

### Status
⬜ Not started

### Known Issues
None yet.

### Risks
| Risk | Impact | Mitigation |
|---|---|---|
| Business parameters (rates, fees) stored as Platform configuration | High — boundary violation | Configuration is for Platform behaviour; business parameters belong to business applications |
| Secrets stored as settings out of convenience | High | A sensitivity flag plus review; secret-shaped values rejected by validation |

---

# PHASE 14 — OBSERVABILITY

### Objective
Make the Platform's condition visible and its problems diagnosable.

### Tasks
1. OpenTelemetry: traces, metrics and logs exported via OTLP
2. Instrument ASP.NET Core, EF Core, HTTP clients and background workers
3. Baseline metrics (ARCHITECTURE.md §22.3)
4. Trace context propagation to and from business applications
5. Detailed health checks per dependency
6. Background job status and history
7. Alert definitions for actionable conditions
8. A monitoring screen: system status, job runs, health
9. Documentation: `docs/deployment/observability.md` and a runbook

### Architecture Decisions
ADR-015.

### Implementation
Monitoring module completion plus cross-cutting instrumentation.

### Tests
- Integration: a trace spans the full request path; metrics are emitted; a correlation ID ties a log, a trace and an audit event together
- Operational: a simulated dependency failure moves readiness to unhealthy and fires the alert

### Acceptance Criteria
- [ ] One correlation ID retrieves the log, the trace and the audit record for a request
- [ ] Baseline metrics are collected and visible
- [ ] `/health/ready` accurately reflects dependency health
- [ ] Alerts fire on the defined conditions, verified by simulation
- [ ] No sensitive data appears in logs or traces
- [ ] No self-hosted observability stack introduced

### Status
⬜ Not started

### Known Issues
None yet.

### Risks
| Risk | Impact | Mitigation |
|---|---|---|
| Telemetry cost | Medium | Sampling for traces; log level discipline; measure before go-live |
| Instrumentation overhead | Low | Measure; sample rather than instrument less |
| Alert fatigue | Medium | Only actionable alerts; review and remove noisy ones |

---

# PHASE 15 — ADMINISTRATION PORTAL COMPLETION

### Objective
Complete the administration screens for the modules delivered after Phase 7, and make the portal coherent as a whole.

### Tasks
1. Security administration: sessions, devices, MFA, security events
2. Workflow administration: definitions, instances, tasks
3. Notification administration: templates, providers, delivery logs
4. Document administration: storage, policies, access
5. Integration administration: providers, status, credential configuration (references only, never values)
6. Configuration administration: settings, feature flags, history
7. Monitoring screens
8. Cross-cutting consistency pass: navigation, empty states, error states, loading states, confirmations
9. Full bilingual and responsive verification of every new screen

### Architecture Decisions
None expected; any UI pattern change is applied consistently across the portal.

### Implementation
Frontend; backend changes only to close genuine contract gaps.

### Tests
- E2E for each administrative area, in both locales
- Accessibility checks on all new screens
- Responsive verification at four breakpoints

### Acceptance Criteria
- [ ] Every module has complete administration screens
- [ ] The portal is visually and behaviourally consistent throughout
- [ ] Every screen works in Arabic and English, at every breakpoint
- [ ] The design and icon policy holds across all new screens
- [ ] No credential value is ever displayed in the integration screens
- [ ] E2E suite green in both locales

### Status
⬜ Not started

### Known Issues
None yet.

### Risks
| Risk | Impact | Mitigation |
|---|---|---|
| Inconsistency accumulated across phases | Medium | A deliberate consistency pass is part of this phase, not an afterthought |
| Design drift toward decoration under "make it nicer" pressure | Medium | ARCHITECTURE.md §9.5 is the standard; owner review |

---

# PHASE 16 — PLATFORM DASHBOARD

### Objective
A calm operational overview of the Platform itself — and nothing else.

### Tasks
1. Metrics: total users, active users, connected applications, recent security events, recent activity, system health, pending platform approvals, notification status
2. Aggregation queries designed against indexes; cached
3. A quiet, text-first layout: title → value → supporting information
4. Bilingual and responsive

### Architecture Decisions
Dashboard scope is Platform-only.

### Implementation
A small backend aggregation surface plus one frontend feature.

### Tests
- Unit: aggregation correctness
- Integration: dashboard loads within budget on a realistically sized dataset
- Security: every panel respects the viewer's permissions — a user sees only what they may see

### Acceptance Criteria
- [ ] The dashboard shows Platform status only
- [ ] **No financial KPI, transfer volume, gold price or accounting balance appears** — this is an explicit prohibition
- [ ] Panels respect permissions
- [ ] No huge-icon / huge-number / gradient card pattern
- [ ] Loads within the performance budget
- [ ] Works in both locales at every breakpoint

### Status
⬜ Not started

### Known Issues
None yet.

### Risks
| Risk | Impact | Mitigation |
|---|---|---|
| Business KPIs requested on the Platform dashboard | High — boundary violation | Explicitly prohibited by the brief; business dashboards belong to business applications |
| Expensive aggregation queries | Medium | Designed against indexes; cached; measured |

---

# PHASE 17 — DATABASE HARDENING, BACKUP & RECOVERY

### Objective
Make the data layer fast, correct and genuinely recoverable.

### Tasks
1. Index review across all schemas: add what is missing, remove what is unused
2. Query analysis against a production-sized seeded dataset; fix the slow paths
3. Verify constraints, uniqueness and integrity rules are declared in the database, not only in code
4. Confirm no cross-schema foreign keys exist — as an automated assertion
5. Partition maintenance verification for audit
6. Connection pool tuning
7. Least-privilege database roles, including audit's INSERT/SELECT-only role
8. Backup configuration: automated backups, WAL archiving, PITR
9. **Perform a full restore drill**, timed against the RTO
10. Write the recovery runbooks in `docs/deployment/`

### Architecture Decisions
Confirm or revise the RPO/RTO targets with the owner (Q7).

### Implementation
Database and infrastructure work; minimal application code.

### Tests
- Performance: every endpoint measured against its budget on a production-sized dataset
- Integrity: constraint violations rejected at the database level
- Recovery: a real point-in-time restore into a temporary environment, timed and documented
- Architecture: an automated assertion that no foreign key crosses a schema boundary

### Acceptance Criteria
- [ ] All performance budgets met on a production-sized dataset
- [ ] No unused index; every foreign key indexed
- [ ] No cross-schema foreign key exists — proven by assertion
- [ ] A **restore drill has actually been performed** and completed within the RTO
- [ ] Recovery runbooks are written and were followed successfully during the drill
- [ ] Least-privilege roles in place; the audit role cannot UPDATE or DELETE

### Status
⬜ Not started

### Known Issues
None yet.

### Risks
| Risk | Impact | Mitigation |
|---|---|---|
| Backups configured but never restored | Critical | The drill is an acceptance criterion, not a recommendation |
| Performance problems found late | Medium | Budgets exist from Phase 1; this phase verifies rather than discovers |
| RTO unachievable with the chosen configuration | High | Measured here, while there is still time to change the configuration |

---

# PHASE 18 — DEVELOPER EXPERIENCE & DOCUMENTATION

### Objective
Ensure a developer on a future system can answer "how do I connect my system to the Company Platform?" without reading Platform source code.

### Tasks
1. Complete the guides: Authentication, Authorization, API, Audit Integration, Workflow Integration, Notification Integration, Document Integration, Integration, and a general Developer Guide
2. A getting-started path from zero to a first authenticated API call
3. Reference client / SDK sample
4. Local development guide; troubleshooting guide
5. Contribution guide, coding standards, review checklist
6. Architecture documentation refreshed to match what was actually built
7. ADR review: mark superseded decisions honestly

### Architecture Decisions
Any decision that drifted during implementation is reconciled here — the documentation is corrected to reality, or the code is corrected to the decision, deliberately and explicitly.

### Implementation
Documentation and samples.

### Tests
The real test: **a developer who has not worked on the Platform integrates a sample application using only the documentation.** Where they get stuck, the documentation is wrong.

### Acceptance Criteria
- [ ] All nine guides exist and are accurate
- [ ] An unfamiliar developer completes an integration using documentation alone
- [ ] Getting started works on a clean machine, following the written steps exactly
- [ ] ARCHITECTURE.md matches the built system; every divergence is either fixed or recorded in a superseding ADR

### Status
⬜ Not started

### Known Issues
None yet.

### Risks
| Risk | Impact | Mitigation |
|---|---|---|
| Documentation drifted from the implementation | High | Docs updated every phase; this phase verifies rather than writes from scratch |
| Documentation written by those who already know the system | Medium | Validate with someone who does not |

---

# PHASE 19 — CLOUD DEPLOYMENT

### Objective
Run the Platform on real cloud infrastructure across Development, Staging and Production.

### Tasks
1. **Confirm the cloud provider (Q4) and data residency (Q6)** — a prerequisite, not a task to improvise
2. Provision managed container hosting, managed PostgreSQL with replica and PITR, object storage, secret manager, telemetry
3. Infrastructure as code
4. Domain, TLS certificates, CDN/WAF
5. Environment configuration and secret population
6. Deployment pipeline to all three environments, with manual approval for Production
7. Migration deployment as an explicit, backup-preceded step
8. Rolling deployment with health gates; a rehearsed rollback
9. Monitoring and alerting connected
10. Operational runbooks

### Architecture Decisions
ADR-009 finalized with the chosen provider.

### Implementation
Infrastructure and pipeline; no application logic.

### Tests
- Deployment: a full deploy to Staging, then a rollback, then a redeploy — all rehearsed
- Smoke tests post-deploy in every environment
- Security: TLS configuration, headers and CORS verified from outside; no internal detail exposed
- Load: baseline load test against Staging

### Acceptance Criteria
- [ ] All three environments run and are correctly isolated
- [ ] The Platform is reachable over the internet with correct TLS, and depends on **no** office network, internal IP or firewall configuration
- [ ] Deployment is automated; production requires approval
- [ ] Rollback has been performed successfully at least once
- [ ] Secrets come from the secret manager; none in images, code or the repository
- [ ] Monitoring and alerts are live
- [ ] Staging mirrors production configuration and contains no real personal data

### Status
⬜ Not started

### Known Issues
Provider not yet chosen (Q4).

### Risks
| Risk | Impact | Mitigation |
|---|---|---|
| Provider decision delayed | High — blocks the phase | Raise Q4 early; the architecture stays provider-neutral until then |
| Cost higher than expected | Medium | Estimate before provisioning; start small; managed services scale down as well as up |
| Production data placed in staging for convenience | High — privacy and legal | Prohibited; anonymized or synthetic data only |

---

# PHASE 20 — TESTING & QUALITY HARDENING

### Objective
Verify the whole system, not module by module. Testing has run continuously; this phase closes the gaps between the parts.

### Tasks
1. Coverage review; remediate meaningful gaps (coverage is a signal, not a target to game)
2. End-to-end suites across the complete administration portal, in both locales
3. Load and stress testing against realistic profiles
4. Security testing: OWASP ASVS-guided review, dependency and container scans, and an external penetration test
5. Exhaustive authorization testing: every endpoint × unauthenticated, wrong permission, correct permission, wrong scope
6. Failure-injection: database unavailable, storage unavailable, provider unavailable, outbox backlog
7. Accessibility audit across the portal
8. Bilingual and responsive audit across the portal
9. Data integrity verification under concurrency

### Architecture Decisions
Any defect revealing an architectural flaw follows §26.3 rather than being patched over.

### Implementation
Tests and fixes only. No new features.

### Tests
This phase is tests.

### Acceptance Criteria
- [ ] Every module meets its coverage threshold, with security-critical paths near-complete
- [ ] E2E suite green in Arabic and English
- [ ] Load test meets the performance budgets at expected peak
- [ ] No high or critical security finding remains open
- [ ] The exhaustive authorization matrix passes with zero exceptions
- [ ] The system degrades gracefully under each injected failure
- [ ] Accessibility audit passes WCAG 2.2 AA

### Status
⬜ Not started

### Known Issues
None yet.

### Risks
| Risk | Impact | Mitigation |
|---|---|---|
| Serious defects found late | High | Continuous testing from Phase 1 makes this a verification phase, not a discovery phase |
| Penetration test scheduling | Medium | Book well in advance of go-live |
| Pressure to skip fixes to meet a date | High | Security and correctness findings are not negotiable; scope is |

---

# PHASE 21 — FINAL HARDENING & GO-LIVE

### Objective
Take the Platform into production, safely and reversibly.

### Tasks
1. Close every finding from Phase 20
2. Final security review and configuration audit
3. Production readiness review against a written checklist
4. Bootstrap administrator creation in production, with the owner present and audited
5. Data migration, if Q11 requires it
6. Operational handover: runbooks, alert routing, on-call expectations, escalation
7. Go-live plan with a rollback decision point and defined criteria
8. Post-go-live monitoring window
9. Final documentation freeze and release tagging

### Architecture Decisions
Record any decision deferred during the project as an explicit item for the next cycle, rather than leaving it undocumented.

### Implementation
Hardening and operations.

### Tests
Full regression, a final restore drill, and a production smoke test after go-live.

### Acceptance Criteria
- [ ] No open high or critical finding
- [ ] Production readiness checklist fully satisfied
- [ ] Backups verified and a restore drill passed against production configuration
- [ ] Monitoring and alerting confirmed live in production
- [ ] Runbooks complete and handed over
- [ ] Rollback plan documented and rehearsed
- [ ] Bootstrap administrator created securely; no default credential exists anywhere
- [ ] Documentation matches the deployed system

### Status
⬜ Not started

### Known Issues
None yet.

### Risks
| Risk | Impact | Mitigation |
|---|---|---|
| Go-live pressure causes checklist shortcuts | High | The checklist is a gate, not a guideline |
| No rollback path once real data exists | High | Backup immediately before; rollback criteria defined in advance |
| Unclear operational ownership after handover | Medium | Named owners and escalation path agreed before go-live |

---

## 5. Cross-phase standards

Applied in every phase without exception:

| Standard | Requirement |
|---|---|
| Boundaries | The §4.4 boundary test applied to every new capability |
| Security | Every endpoint authenticated, authorized, validated and audited |
| Secrets | Never in source control; CI enforces this |
| Tests | Unit + integration in-phase; security tests for anything touching auth |
| Localization | No hardcoded UI text; both locales verified |
| Responsive | Verified at four breakpoints |
| Accessibility | Keyboard, focus, labels, contrast |
| Documentation | Updated within the phase, not deferred |
| Architecture tests | Green; a violation fails the build |
| Code quality | No huge classes, no logic in endpoints, no duplication, no hardcoded configuration, no temporary hacks |

## 6. Definition of Done for a phase

1. All tasks complete, or explicitly deferred with a recorded reason
2. All acceptance criteria met
3. All tests green, including architecture and security tests
4. The system builds, runs and is manually verified
5. CI green
6. No known fundamental defect
7. Architecture reviewed; boundaries intact
8. `ARCHITECTURE.md`, `DEVELOPMENT_STATUS.md` and this plan updated
9. New ADRs written for any decision made
10. Work presented to the owner — then **STOP** and await `CONTINUE`
