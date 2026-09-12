# Company Central Platform — Development Status

| Field | Value |
|---|---|
| Last updated | 2026-09-10 |
| Current phase | **Phase 20 — Testing & Quality Hardening** |
| Phase status | 🟡 **Substantially complete.** Every endpoint is asked to refuse rather than trusted to declare — twice, once anonymous and once signed in holding nothing. It found a live defect. What remains needs an environment or a person this project does not have: a realistic data volume, and an external penetration test. |
| Next phase | **Phase 19 — Cloud Deployment** — ⛔ still blocked on Q4 |
| Blocked | ⚠️ Partially — see §4 |
| Deployed | ✅ **Live on Railway** — API https://company-central-platform-production.up.railway.app · portal https://ccp-frontend-production-3752.up.railway.app |

---

## 1. Overall progress

| # | Phase | Status | Notes |
|---|---|---|---|
| 0 | Analysis | ✅ Complete | Documents and 15 ADRs |
| 1 | Foundation & Platform Kernel | 🟡 **Substantially complete** | Builds, runs, verified manually; integration tests unrun (§4) |
| 2 | Identity & Authentication | 🟡 **Feature-complete** | All planned tasks built: authentication, password management and reset, user administration, `/me`, JWKS, bootstrap seeder. 116 unit tests. Integration tests written but unrun; permission strings declared but not enforced until Phase 4. |
| 3 | Organization | 🟡 **Core complete** | Unit hierarchy with materialized path, arbitrary depth, atomic moves, cycle prevention, bilingual names, employees, positions, company. 48 unit tests. Position/company endpoints and integration tests outstanding. |
| 4 | Authorization & RBAC | 🟡 **Core complete** | RBAC with organizational scope, enforcement wired, anti-escalation, version-stamped cache, application registry, permission declaration. Scope filter applied at the data layer for employee search. 63 unit tests. |
| 5 | Security Hardening | 🟡 **Core complete** | Per-endpoint rate limits, TOTP two-factor with recovery codes, AES-256-GCM secret protection, step-up authentication enforced on six privileged endpoints, security event log with bounded search. 56 unit tests, 3 new architecture tests. |
| 6 | Audit | 🟡 **Core complete** | Append-only trail, monthly range partitioning with a maintenance job, redaction before storage, internal and external ingestion, bounded search, privileges revoked to INSERT+SELECT at the database. **Wired into 15 state-changing handlers across Identity, Organization, Authorization and Security**, through a neutral kernel seam so no module references Audit. 50 unit tests + 3 architecture tests. |
| 7 | Frontend Foundation & Core Admin UI | 🟢 **Complete** | Next.js 15 / React 19 / TypeScript strict / Tailwind 4 / next-intl. White-and-blue token set, no icon package installed at all. Arabic-first with full RTL mirroring by logical properties, 98 catalogue keys at parity. BFF with httpOnly session, no token in the browser. Shell, DataTable, form primitives. Nine screens: sign-in, MFA, forgot/reset password, dashboard, users, employees, roles, audit. Lint rules enforce the RTL and no-hardcoded-string criteria. Production build green in both locales. Twelve screens, every one of them able to write: company setup, organizational structure, employees, users, role definition and permission editing, role granting with a step-up prompt, own-account two-factor enrolment, and a dashboard of live figures. Types generated from the OpenAPI document the API emits. Playwright sweeps every screen in both locales and passes. |
| 8 | Workflow | 🟢 **Complete** | A reusable approval engine holding no business rule — verified by a test that fails the build if business vocabulary appears in the module at all. Definitions as versioned data, registered by applications through the API with no Platform code change. Versions frozen once published; instances run on the version they started with. Six organizational assignee strategies plus a caller-supplied list, which is where business-conditional routing lives — outside the engine. Approve, reject, return, delegate, comment, cancel; first to act settles a step and the rest are withdrawn. Service levels escalated once by a background sweep that raises an event and does not reassign. Task inbox and administrator view in both locales. Integration tests walk a whole approval on a real database. |
| 9 | Notifications | 🟢 **Complete** | Templates per locale with declared variables and escaping that cannot be opted out of; no template language, substitution only. In-app and email, with `INotificationChannelProvider` as the seam — adding SMS is one interface and one registration. Retry, backoff with jitter, and giving up live in the dispatcher so every channel behaves the same when a vendor is down. Per-user, per-category, per-channel preferences, with security refused at the resolver and at creation. Delivery log keeps permanent failures visible. Listens to Workflow and Identity, neither of which knows it exists. 23 unit tests, 6 integration tests. |
| 10 | Documents | 🟢 **Complete** | Metadata in PostgreSQL, bytes in object storage, behind `IDocumentStorageProvider` — a directory on disk in development, S3-compatible in production, chosen by what is configured rather than by the environment name. Uploads are identified by reading their first bytes: an executable renamed to `report.pdf` is refused and named. Random object keys, SHA-256, size enforced during the copy rather than after it. A scanner hook whose default reports *NotScanned* rather than *Clean*, so an audit of what was checked tells the truth. Versions are added, never edited. Access decided by one evaluator used by every path — user, role or unit rules that add up rather than override — and every access logged, **including the refusals**. Two-stage deletion with a thirty-day grace period and a purge sweep that destroys bytes before it marks the record. Polymorphic linking with no foreign key, so a business system files a document against its own record. Screens in both locales, and an upload control any screen can embed. 47 unit tests, 7 integration tests. |
| 11 | External API Platform & App Registry | 🟢 **Complete** | Client credentials with rotation: an application holds two live secrets at once, so the new one works before the old one stops and a rotation is never an outage. `LastUsedAt` on every exchange, because finishing a rotation needs evidence rather than nerve. Applications hold the **same roles at the same scopes** as people, resolved by the same evaluator — two grant tables, one algorithm, and no second vocabulary of API scopes to keep in step. Acting on behalf of a person is the **intersection** of what the application and that person may do. The subject claim carries its kind, so no handler can mistake an application for a person. Per-application rate limits; the token endpoint partitioned by client id. The published contract states every endpoint's permission, step-up requirement and anonymity, derived from the endpoint metadata. `Deprecation`/`Sunset` headers exist with nothing yet deprecated. A permission manifest endpoint whose namespace comes from the token, so a system can only ever declare its own. 24 unit tests, 8 integration tests, an integration guide and a reference client. |
| 12 | Integrations | 🟢 **Complete** | Every outbound call passes one door: the address is checked against a **deny-by-default** allow-list, the credential is resolved from a *reference* so no column in the module could hold a secret, the request runs under a resilience pipeline built from the provider's own settings, and both halves are written to the call log with the provider's declared fields blanked **before storage**. The SSRF defence checks the name and every address it resolves to — the metadata service, the private ranges, non-HTTP schemes and credentials in the URL are all refused, and a caller is never told which check failed. Bulkhead, breaker, retry with jitter, timeout, in that order, because the order decides what each one protects. Inbound webhooks verify an HMAC over the raw body with the timestamp inside the signed material, and every accepted signature is remembered so the same request cannot be replayed. Health is derived from recent calls, never stored. Retention from the first day. 48 unit tests. |
| 13 | Configuration & Feature Flags | 🟢 **Complete** | Settings are declared before they are set, in their owner's namespace, with a type and constraints checked at the moment somebody types a value. **A value that looks like a secret is refused outright** — a settings table is stored in plaintext, exported and backed up, and the sensitivity flag stops a value being read back rather than stopping it being there. Three scopes with narrowest winning; a row exists only where somebody overrode something. Every change keeps what it was, what it became, who and why — and for a sensitive setting says that it changed and not what to. Cached on a version stamp held in the database, so a change takes effect on the very next request and on every instance. Feature flags targeted by role or unit and nothing else, off by default, off when undeclared, and off meaning off however they are targeted. 52 unit tests. |
| 14 | Observability | 🟡 **Partly complete** | OpenTelemetry traces and metrics, exported over OTLP where an endpoint is configured and instrumented unconditionally where one is not — so the code path in production is the one that ran locally. The correlation id is written onto the span by the middleware that decides it, so one identifier retrieves the log line, the trace and the audit record. Credentials are removed from log events **at the sink**, by name and by shape, because discipline does not scale to every log statement anybody will ever write. Five instruments the framework cannot supply, each with an alert defined against it. Readiness now distinguishes unhealthy from degraded: a bucket nobody can reach stops documents, not the Platform. 20 unit tests, a runbook. **No monitoring screen and no stored job history** — those belong with the dashboard. |
| 15 | Administration Portal Completion | 🟢 **Complete** | The two modules that had a complete API and no screen now have one. Integrations opens on the question an operator asks during an incident — is this provider working — with health first and the configuration below it; `Idle` is grey rather than green, because nothing has been asked of a provider that has not been called and a green light nobody earned is worse than an honest blank. Failures are shown as *3 of 4* rather than 75%, since the percentage hides how small the sample is. The call log shows both payloads in full, which is only safe because they were redacted **before** they were stored — what an administrator reads is what the database holds, and there is no unredacted copy for the next export to find. **No credential value appears on either screen, and not because the screen hides one**: the Platform stores a secret's *name* and has no field that could carry a value. Configuration shows what is in force, how many scopes overrode it, and the change history — what it was, what it became, who and why — and for a sensitive setting says that it changed without saying what to. Pasting a credential into a setting is refused by the Platform, and the screen explains the refusal in full rather than reporting it as a validation quibble. The health stamp records when health was **read**, not when the page last rendered, so the figure goes stale in front of whoever is watching it. Both locales, both directions, in the accessibility, responsive and signed-in sweeps.
| 16 | Platform Dashboard | 🟢 **Complete** | Background jobs keep a history, and it exists because writing the screen found that they kept nothing. Phase 14 declared `ccp.jobs.runs`, wrote an alert against it, and put the meter in a project no module's Infrastructure references — so not one of the five sweeps could call it, and the alert sat permanently green on jobs that might never have run. The meter moved to the application layer; every periodic sweep now runs through one `JobRunner` that times the pass, records the outcome, swallows what it throws so a bad pass cannot retire the timer for the life of the process, and tells a shutdown apart from a failure so a deployment does not read as an outage. Runs are stored in the kernel schema through a neutral seam, in their own transaction — the record of a failed pass must not roll back with the pass — and pruned by the journal itself rather than by a sixth job whose failure nothing would record. Each row carries what the pass **did**, in the job's own words, because "removed 412" and "removed 0" are different facts and a history that cannot tell them apart cannot tell a working sweep from one whose query quietly stopped matching. The screen shows every job plus the outbox, where the figure given the most room is the **age** of the oldest undelivered message rather than the depth. A job that stopped running a week ago still appears and turns red instead of vanishing, which has an integration test on it. 9 unit tests, 8 integration tests.
| 17 | Database Hardening, Backup & Recovery | 🟡 **Core complete** | `statement_timeout` and `idle_in_transaction_session_timeout` are enforced **by PostgreSQL**, not by a client-side command timeout that stops the application waiting while the query carries on burning the server's CPU. They are applied to the connection string in one place, because there are twenty-two `UseNpgsql` call sites and a rule repeated twenty-two times is missing from at least one. The migrator is exempt on purpose — an index build is legitimately long, and the subtler half is that its advisory-lock connection is exempt too, since `pg_advisory_lock` blocks and a statement timeout applies to a blocking statement, so a second instance queuing behind a long migration would be cut off and then serve requests against a half-migrated schema. The outbox finally prunes delivered rows (the oldest open debt, #4), **never dead-lettered ones**, because those are events that will never arrive and a timer must not erase the evidence. `docs/deployment/backup-and-recovery.md` covers what is at stake, what to back up — including the secrets, which are in neither the database nor the bucket and whose absence makes every enrolled second factor undecryptable — how to restore, and a drill. `scripts/verify-restore.sh` answers what a dump file cannot: it restores into a scratch database and asserts the audit trail is still partitioned and that somebody can still sign in. 9 unit tests. **No backup is being taken** — that is a provider feature and the provider is undecided (Q4).
| 18 | Developer Experience & Documentation | 🟢 **Complete** | The repository had **no `README.md`** — seventeen phases, forty-five documents, and nothing at the front door. It has one now, and it points at `DEVELOPMENT_STATUS.md` as the honest account rather than claiming completeness itself. The bigger find was that `getting-started.md` still described Phase 1: it said there were no capability modules yet, that the frontend arrived later, and it applied **one** migration where there are twelve — so a new developer following it word for word would get a Platform that starts, reports healthy, and answers 500 from every module. That failure is now the first entry under common problems. The guide also gained the portal, the module test suites, the contract regeneration step that CI enforces, and the first-administrator bootstrap. Both documentation indexes were rewritten: one listed as *planned* several documents that exist, and stated that the .NET SDK was not installed on this machine. `scripts/check-doc-links.py` now fails the build on a broken relative link, and it was tested against a deliberately broken one before being trusted — a guard that passes on its first run is the exact shape of the two dead instruments found last phase.
| 19 | Cloud Deployment | ⬜ Not started | Blocked on provider decision (Q4) |
| 20 | Testing & Quality Hardening | 🟡 **In progress** | Taken out of order because it is the only remaining phase that needs no provider decision. Three suites: the organization hierarchy against a real database (#12), permission resolution against a real database (#16), and **the exhaustive authorization matrix** — every endpoint in the route table called with no credentials and then with a forged token, asserting 401 from each. That last one closes a gap nobody had named: an architecture test proves every endpoint *declares* a permission, which is a statement of intent that a misordered middleware or a group missing `RequireAuthorization` would leave entirely unhonoured, with both tests still green. The list comes from the running server's own route table, so an endpoint added next year is covered the day it is mapped. Then two more, added after the first CI run came back: the **outbox retention sweep**, whose four most important assertions are about rows it must *not* delete — a dead letter is an event that will never arrive, and a timer that quietly removed those would erase the evidence of the one failure the outbox exists to make visible — and **failure injection on readiness**, which proves that an unreachable document store degrades the Platform rather than stopping it. That last reproduces, deliberately, the shape of the Phase 10 outage: a storage check reporting *unhealthy* would take every instance out of rotation over a bucket. **The failure injection immediately found a live defect** (#73): readiness had been answering 503 to an unreachable bucket for three phases, because `failureStatus: Degraded` on the registration is ignored when a check catches its own exception and returns a result — so the Platform would have emptied itself out of the load balancer over object storage, exactly the outage Phase 10 taught it not to have. The matrix has a second column too: a signed-in account holding **nothing**, refused with 403 by every permission-gated endpoint — which catches a different mistake, since refusing an anonymous caller only proves the authentication middleware runs, and an endpoint mapped with a bare `RequireAuthorization()` would admit every employee in the company. The accessibility baseline moved to **WCAG 2.2 AA**, which is what the phase asks for. Load testing exists as an executable budget and has never been run against realistic data (#74); the index review (#67) and an external penetration test (#75) need the same environment or a person this project does not have.
| 21 | Final Hardening & Go-Live | ⬜ Not started | |

**Completed: 1 of 22 phases. Phases 1–15 in progress.**

Legend: ✅ complete · 🟡 in progress · ⬜ not started · ⛔ blocked

---

## 2. Module status

No capability module has been implemented — correct for Phase 1, which builds
the foundation the modules sit on. All eleven are specified in
[ARCHITECTURE.md §7.2](ARCHITECTURE.md).

| Module | Specified | Implemented | Tested | UI | Documented |
|---|---|---|---|---|---|
| Kernel (not a module) | ✅ | ✅ | ✅ | — | ✅ |
| Identity | ✅ | ✅ | 🟡 116 unit / 37 integration unrun | ⬜ | ✅ |
| Organization | ✅ | 🟡 Core | 🟡 48 unit tests | ⬜ | ✅ |
| Authorization | ✅ | 🟡 Core | 🟡 63 unit tests | ⬜ | ✅ |
| Security | ✅ | 🟡 Core | 🟡 56 unit / 15 integration unrun | ⬜ | ✅ |
| Audit | ✅ | ✅ | ✅ 50 unit / 5 integration | ⬜ | ✅ |
| Workflow | ✅ | ⬜ | ⬜ | ⬜ | ⬜ |
| Notifications | ✅ | ⬜ | ⬜ | ⬜ | ⬜ |
| Documents | ✅ | ⬜ | ⬜ | ⬜ | ⬜ |
| Integrations | ✅ | ⬜ | ⬜ | ⬜ | ⬜ |
| Configuration | ✅ | ⬜ | ⬜ | ⬜ | ⬜ |
| Monitoring | ✅ | ⬜ | ⬜ | ⬜ | ⬜ |

---

## 3. Phase 1 report

### 3.1 What was built

**Solution structure** — 5 source projects, 3 test projects, layered per
ARCHITECTURE.md §8.2, with project references wired so that a forbidden
dependency cannot be added by accident.

**Kernel** (`CCP.Kernel`) — `Result`/`Result<T>`, `Error`, `ErrorType`, `Guard`,
`IClock`/`SystemClock`, UUID v7 (RFC 9562), `Entity`/`AggregateRoot`,
`IDomainEvent`/`IIntegrationEvent`, `PageRequest`, `SortSpec`, `PagedResult<T>`.

**Application** (`CCP.Kernel.Application`) — `ICurrentUser`, `IRequestContext`,
`IUnitOfWork`, `IOutbox`, `IIntegrationEventHandler<T>`,
`IIntegrationEventDispatcher`, `IPlatformModule`.

**Infrastructure** (`CCP.Kernel.Infrastructure`) — `KernelDbContext` on the
`kernel` schema, snake_case naming convention, the transactional outbox
(`OutboxMessage`, `OutboxWriter`, `OutboxRelay`, `IntegrationEventDispatcher`),
and the initial EF migration.

**Api** (`CCP.Kernel.Api`) — RFC 9457 `ProblemDetailsFactory`, exception
boundary, correlation-id middleware, security headers, `ResultExtensions`,
`IModuleEndpoints`, `RequirePermissionAttribute`.

**Host** (`CCP.Api.Host`) — composition root, explicit module registry, the
middleware pipeline of §8.3, typed configuration validated at startup, Serilog,
health checks, rate limiting, strict CORS, OpenAPI, and the `Diagnostics`
vertical slice.

**Operations** — `docker-compose.yml` (PostgreSQL 17, MinIO, Mailpit),
`build/api.Dockerfile` (multi-stage, non-root), `.github/workflows/ci.yml`,
`.gitignore` written before the first commit, `.editorconfig`, `global.json`.

### 3.2 Design decisions taken during implementation

| Decision | Reason |
|---|---|
| Service parameters marked `[FromServices]` explicitly | Minimal-API inference depends on what happens to be registered at mapping time, which made endpoint mapping fail under a different composition. Explicit is stable (P9). Found by the architecture test. |
| Architecture tests written with plain reflection rather than NetArchTest | Zero dependency risk, and the assertions read as the rules themselves. ARCHITECTURE.md §6.4 allows "or equivalent". |
| `dotnet format` and analyzer rules enforced with warnings-as-errors | A warning that is not an error is a warning nobody fixes. |
| Analyzer suppressions carry written justifications in `.editorconfig` | A suppression without a reason is technical debt with no owner. |
| No mediator library | ADR-002. Plain handler classes until pipeline behaviours prove their value. |

### 3.3 Files created

79 files. The significant ones:

| Path | Contents |
|---|---|
| `CompanyCentralPlatform.sln` | 8 projects |
| `src/Kernel/**` | 4 projects, 17 source files |
| `src/Host/CCP.Api.Host/**` | Composition root, module registry, diagnostics slice |
| `tests/**` | 3 projects, 66 passing tests + integration suite |
| `docker-compose.yml`, `build/api.Dockerfile`, `.dockerignore` | Local and container runtime |
| `.github/workflows/ci.yml` | Build → test → architecture → integration → format → secret scan → dependency audit → image scan |
| `.gitignore`, `.editorconfig`, `global.json`, `Directory.Build.props` | Repository standards |
| `.env.example` | Placeholder configuration; no real secret |
| `docs/development/getting-started.md` | Clone to running system |

### 3.4 Files modified

`ARCHITECTURE.md` (unchanged — the built system matches it), `PROJECT_PLAN.md`
(Phase 1 status), `DEVELOPMENT_STATUS.md` (this file).

### 3.5 Tests

| Suite | Tests | Result |
|---|---|---|
| `CCP.Kernel.UnitTests` | 53 | ✅ All pass |
| `CCP.Architecture.Tests` | 13 | ✅ All pass |
| `CCP.Api.IntegrationTests` | 30 written | ⚠️ **Not executed** — no database available (§4) |
| **Total** | **96** | **66 executed and passing** |

Release build: clean, zero warnings.

**The architecture tests were verified to actually fail.** Introducing a real
violation (`CCP.Kernel.Api` referencing `CCP.Kernel.Infrastructure` and using a
type from it) made `Api_DoesNotDependOnInfrastructure` fail; reverting made it
pass. A guard that cannot fail guards nothing, so this was checked rather than
assumed.

### 3.6 Manual verification

The API was run and every diagnostics endpoint exercised over HTTP:

| Check | Result |
|---|---|
| `/api/v1/diagnostics/ping` | ✅ 200, correlation id in header and body |
| Inbound `X-Correlation-Id` echoed | ✅ `my-trace-42` returned unchanged |
| Correlation id sanitised | ✅ `abc<script>alert(1)</script>` → `abcscriptalert1script` |
| Security headers | ✅ CSP, X-Content-Type-Options, X-Frame-Options, Referrer-Policy all present |
| Validation error | ✅ 422, `application/problem+json`, both field errors listed |
| Not found / conflict / forbidden | ✅ 404 / 409 / 403 with correct codes |
| Unhandled exception | ✅ 500, no stack trace, no exception message, no file path — correlation id retained |
| Paging envelope | ✅ Correct items, page, totals |
| Page size cap | ✅ `pageSize=5000` → 422 `PLATFORM.INVALID_PAGE_SIZE` |
| Sort allow-list | ✅ `sort=passwordHash` → 422 `PLATFORM.SORT_FIELD_NOT_ALLOWED` |
| `/health/live` | ✅ 200 `Healthy`, no internal detail |
| `/health/ready` | ✅ 503 `Unhealthy` with the database unreachable — the check works |
| Outbox relay resilience | ✅ Logged the database failure and kept running rather than dying |

### 3.7 Defects found and fixed during the phase

| # | Defect | How it was found | Fix |
|---|---|---|---|
| 1 | **Columns generated as PascalCase**, so the partial index filters (written in snake_case) referenced columns that did not exist — the indexes would have failed at migration time | Reading the generated SQL rather than trusting it | `ApplySnakeCaseNames()` was defined but never called in `OnModelCreating`. Called it; regenerated the migration. |
| 2 | Endpoint service parameters bound by inference, failing under a different service composition | Architecture test | Explicit `[FromServices]` |
| 3 | `Results` namespace collided with ASP.NET Core's `Results` class | Compiler | Explicit alias, with a comment explaining why |
| 4 | `IAsyncLifetime` (Task) conflicted with `WebApplicationFactory.DisposeAsync` (ValueTask) | Compiler | Explicit interface implementation |
| 5 | Docker `HEALTHCHECK` used `curl`, absent from the aspnet runtime image | Review of the Dockerfile | Removed; the container platform probes `/health/live` over HTTP instead |
| 6 | **Error responses carried no security headers.** The exception boundary calls `Response.Clear()` before writing Problem Details, which stripped the CSP, `nosniff`, `X-Frame-Options` and `Referrer-Policy` headers set earlier in the pipeline. Every 4xx and 5xx response was served unprotected. | Checking the headers on a 500 response instead of assuming they matched a 200 | Security headers now applied via `Response.OnStarting`, so they are written at the last possible moment and survive any reset downstream. Regression test added covering 500, 404 and 422. |

Two defects are worth dwelling on.

**Defect 1** would have surfaced as a failed migration on first deployment, and
was caught only by reading the generated SQL rather than trusting it.

**Defect 6 is the more serious of the two.** An error response is precisely the
kind an attacker provokes deliberately, and it was the one response class
shipping without a Content-Security-Policy or framing protection. It passed the
existing header tests because those tests only exercised a successful response —
a reminder that a test asserting the happy path proves nothing about the others.

### 3.8 Limitation discovered in the architecture tests

Reference-based checks do not detect a dependency whose **only** use is a
`const`, because the compiler inlines const values and prunes the reference.
This is acceptable — copying a literal creates no runtime coupling — but it
means these tests prove the absence of a *binary* dependency, not of every
textual reference. Recorded in the test file so the limitation is not
rediscovered later as a surprise.

---

## 3A. Phase 2 report (in progress)

### 3A.1 What was built

**Domain** — `User` (lifecycle, lockout, email/username rules), `UserStatus`,
`LockoutPolicy` (progressive delay), `UserCredential`, `PasswordHistoryEntry`,
`PasswordPolicy`, `Session`, `RefreshToken` (rotation and family model),
`LoginAttempt`, `IdentityErrors`, and ten integration events.

**Application** — `SignInHandler`, `RefreshTokenHandler`, `SignOutHandler`;
ports for hashing, tokens, breach screening and persistence; `IdentityOptions`.

**Infrastructure** — `Argon2PasswordHasher` (Argon2id, self-describing hashes,
transparent rehash on cost increase), `JwtTokenService` (RS256),
`FileSigningKeyProvider`, `RefreshTokenGenerator`, `IdentityDbContext`,
`IdentityRepository`, `IdentityOutbox`, `IdentityUnitOfWork`, and the
`InitialIdentitySchema` migration.

**Api** — `/api/v1/auth/login`, `/refresh`, `/logout`, with request validation
and explicit access declarations.

**Host** — JWT bearer authentication wired to the module's signing key; the
Identity module registered in the explicit module list.

### 3A.2 The security properties actually implemented

| Property | How |
|---|---|
| Uniform sign-in failure | Unknown username, wrong password, disabled and locked accounts all return `IDENTITY.INVALID_CREDENTIALS`. The real reason goes to login history and the outbox. |
| Uniform sign-in timing | When the username is unknown, a hash verification still runs against a dummy hash computed once at startup. Without it, an unknown username returns in microseconds and a real one in ~100 ms — which enumerates the directory. |
| State checked *after* the password | Checking "is this account disabled" first would leak the account's existence through timing. |
| Progressive lockout | Delay doubles past a free allowance and is capped, so an attacker is slowed without being handed a denial-of-service tool against any known username. |
| Refresh rotation + reuse detection | A spent token presented again revokes the whole token family and its session, and raises a security event. This converts a silent stolen-token compromise into a detected incident. |
| Server-side sign-out | Revokes the session and every refresh token belonging to it. Deleting a cookie is not signing out. |
| Account re-checked on refresh | An account disabled after sign-in cannot extend its session. |
| Hashes and tokens never stored in the clear | Argon2id for passwords; SHA-256 for refresh tokens, whose 256-bit random input needs no memory-hard hashing. |
| Transparent hash upgrade | `NeedsRehash` upgrades a credential at sign-in, the only moment the plaintext is known. |
| No signing key in the repository | Configuration carries a path. Outside Development a missing key is a fatal startup error rather than a silently generated one. |

### 3A.3 Tests

| Suite | Tests | Result |
|---|---|---|
| `CCP.Modules.Identity.UnitTests` | 116 | ✅ All pass |
| `CCP.Kernel.UnitTests` | 53 | ✅ All pass |
| `CCP.Architecture.Tests` | 16 | ✅ All pass |
| **Executed total** | **185** | **All passing** |
| `CCP.Api.IntegrationTests` | 37 written | ⚠️ **Never run** — no database (§4) |

Covered: Argon2id round-trip, salt uniqueness, fail-closed on malformed hashes,
verification across a cost increase, rehash detection, Unicode and long
passwords; lockout progression and its overflow guard; account state
transitions; refresh-token and session state machines; password policy;
username and email validation; JWT issuance, tampering, wrong key, wrong
audience, expiry and malformed input.

### 3A.4 Defects found and fixed during this phase

| # | Defect | How found | Fix |
|---|---|---|---|
| 1 | **Error responses carried no security headers.** The exception boundary calls `Response.Clear()`, stripping the CSP, `nosniff`, framing and referrer headers. Every 4xx and 5xx was served unprotected. | Checking headers on a 500 rather than assuming they matched a 200 | Headers now applied via `Response.OnStarting`, so they survive any reset. Regression test added for 500, 404 and 422. |
| 2 | **The outbox would not have been transactional.** Identity wrote entities through its own DbContext while the outbox wrote through the kernel's — two transactions, so a crash between them could lose an event for a change that happened, or record one for a rollback. | Noticed while wiring the repository | Each module now maps `kernel.outbox_messages` into its own context (`ExcludeFromMigrations`), so one `SaveChanges` commits both. Module-scoped `IIdentityOutbox`/`IIdentityUnitOfWork` prevent DI from binding the wrong context. |
| 3 | **`POST /auth/logout` declared no access rule.** `RequireAuthorization()` demands authentication but states no permission, so the endpoint-security guard correctly rejected it. | Architecture test | Added `[AuthenticatedUserOnly]` with a written reason, so "authenticated, no permission" is declared rather than implied. |
| 4 | **A malformed request body returned 500.** JSON deserialization can leave a non-nullable string null, so the record's signature was no guarantee. | Manual probe with `{"bad":"shape"}` | Request validation before the handler; malformed bodies now return 422 with the standard contract. Also bounds password length, so an oversized password cannot make the server do Argon2id work. |
| 5 | JWT tests issued tokens at a fixed past timestamp and were already expired when validated. | Test failure | Validation tests issue at current time. |

Defects 1 and 2 are the substantive ones. Both were silent: nothing failed, and
both would have reached production looking correct.

### 3A.5 Completed after the first Phase 2 report

**Password management** — `PasswordSetter` as the single place a password is
set, so policy, breach screening, reuse, history pruning and session revocation
cannot be forgotten by any of the three callers. `ChangePasswordHandler`
(requires the current password even when authenticated),
`RequestPasswordResetHandler` and `ResetPasswordHandler`.

**Password reset** — `PasswordResetToken`: hashed, single-use, 30-minute
lifetime, superseded when a newer one is issued or the password changes by
another route. The forgot endpoint always returns 202, whatever the input.

**User administration** — create (always `MustChangePassword`), update,
enable, disable (revokes every session), unlock, search with paging and an
allow-listed sort, and get-by-id.

**Current user** — `/me`, `/me/sessions`, `/me/login-history` (failures
included, so a user can see attempts they did not make).

**JWKS** — `/api/v1/.well-known/jwks.json`, public parameters only. The DTO has
no field capable of carrying a private component, so leaking one is impossible
by construction rather than by care.

**Breach screening** — k-anonymity range check sending only the first five hex
characters of the SHA-1 hash. Fails open and logs; **off by default**, because
enabling an outbound third-party call is the owner's decision.

**Bootstrap administrator** — refuses to run if any user exists, off by
default, no defaults, policy applies, must change at first sign-in, logged at
warning level. Procedure documented; **still needs Q10**.

**Documentation** — `docs/security/authentication.md` and
`docs/identity/bootstrap-administrator.md`.

### 3A.6 Endpoints delivered

| Method | Path | Access |
|---|---|---|
| POST | `/api/v1/auth/login` | anonymous |
| POST | `/api/v1/auth/refresh` | anonymous |
| POST | `/api/v1/auth/logout` | authenticated |
| POST | `/api/v1/auth/password/change` | authenticated |
| POST | `/api/v1/auth/password/forgot` | anonymous |
| POST | `/api/v1/auth/password/reset` | anonymous |
| GET | `/api/v1/.well-known/jwks.json` | anonymous |
| GET | `/api/v1/me` · `/me/sessions` · `/me/login-history` | authenticated |
| GET/POST | `/api/v1/users` | `platform.users.view` / `.create` |
| GET/PUT | `/api/v1/users/{id}` | `platform.users.view` / `.edit` |
| POST | `/api/v1/users/{id}/enable|disable|unlock` | `platform.users.edit` |

### 3A.7 Additional defects found

| # | Defect | How found | Fix |
|---|---|---|---|
| 6 | Three new anonymous endpoints were added without review | Architecture test rejected them | Each added to the reviewed public surface with a written justification. The guard did exactly its job. |
| 7 | User search used `ToLower().Contains()`, which is culture-dependent and applies a function to every row | Analyzer | PostgreSQL `ILIKE`, with LIKE metacharacters escaped. Noted that the leading wildcard precludes a B-tree index; a `pg_trgm` index is the fix if it ever matters (Phase 17). |

### 3A.8 Still not done

- **Permissions are declared but not enforced.** Every administrative endpoint
  carries `[RequirePermission("platform.users.…")]`, and nothing evaluates it
  yet — the handler arrives with the Authorization module in Phase 4. Until
  then those endpoints require authentication only, so **any signed-in user can
  administer users**. This is the single most important thing to know about the
  current state.
- **Reset emails are not sent.** The event is staged on the outbox; the
  Notifications module (Phase 9) is what delivers it. Reset is therefore not
  usable end to end.
- **MFA** — Phase 5.
- **Per-endpoint rate limits** — Phase 5. The global limit is far too generous
  for a login endpoint.
- **Key rotation** never exercised.
- **Integration tests** written (37) but never run.

---

## 3B. Phase 3 report (Organization)

### 3B.1 The decision that shaped the module

**Departments, sections and centers are one entity, not three.** The brief names
them separately; modelling them as three tables would have been the literal
reading and the wrong one. Three near-identical tables mean every hierarchy
query becomes a union of three, "everything under this unit" stops being a
single indexed prefix scan, and a fourth kind of unit needs a schema change.

One `OrganizationUnit` with a type gives arbitrary nesting for free and makes a
new unit kind an enum value. The brief's own requirement — extensible structure,
no fixed number of departments or centers — is better served this way than by
encoding today's three levels into the schema.

### 3B.2 What was built

**Domain** — `OrganizationUnit` (adjacency list + materialized path, move with
cycle prevention, descendant rebasing), `LocalizedName` (both languages
required), `Company`, `Position`, `Employee`, `ManagementChain` (reporting-line
cycle prevention), `OrganizationErrors`, eight integration events.

**Application** — create/move/rename/deactivate unit, unit tree, create/transfer
employee, link user account, employee search with subtree scoping,
`OrganizationMapper` (hand-written, including tree assembly).

**Infrastructure** — `OrganizationDbContext` with the `organization` schema,
`OrganizationRepository`, outbox and unit of work bound to the module's own
context, `InitialOrganizationSchema` migration.

**Api** — nine endpoints, each declaring its permission.

**Documentation** — `docs/identity/organization.md`.

### 3B.3 Why the materialized path is the centre of the design

From Phase 4, authorization scope (`Unit`, `UnitAndBelow`) resolves on **every
request**. A recursive query would put a tree walk on the hot path of every call
in the company; a prefix scan on an indexed text column does not.

Three details make it work, and each is tested:

- **Trailing separator.** Without it, the path of unit `ab` prefix-matches unit
  `abc`, and a scope check silently includes a unit it should not.
- **`text_pattern_ops` index.** The default collation's operator class cannot
  serve a `LIKE 'prefix%'` query, so without this the index would exist and
  never be used.
- **Atomic moves.** A move rewrites every descendant's path in one
  `SaveChanges`. A partial rewrite would leave units claiming an ancestry they
  no longer have, and authorization would then resolve against a tree that does
  not exist. Descendants are loaded **before** the move, because afterwards the
  prefix no longer matches.

### 3B.4 Boundary held

`Employee` carries no salary, leave balance, contract or appraisal. Those are HR
business data (ADR-005 §4.3a). The DTO is the visible edge of that boundary and
is hand-written for exactly that reason — an automatic mapper would add fields
as the entity gained them, silently.

`Employee.UserId` points at `identity.users` with **no foreign key**, verified by
the schema-boundary architecture test now covering all three module contexts.

### 3B.5 Tests

| Suite | Tests | Result |
|---|---|---|
| `CCP.Kernel.UnitTests` | 53 | ✅ |
| `CCP.Architecture.Tests` | 18 | ✅ |
| `CCP.Modules.Identity.UnitTests` | 116 | ✅ |
| `CCP.Modules.Organization.UnitTests` | 48 | ✅ |
| **Executed total** | **235** | **All passing** |

Organization coverage: path construction and prefix-match safety, arbitrary
depth to ten levels, containment in every direction, moves including the
under-own-descendant rejection, descendant rebasing across depth changes and an
eight-level subtree, the rebase guard against rewriting an unrelated unit, code
validation including the path separator, reporting-line cycles (direct,
indirect, and pre-existing corrupt data), and tree assembly including orphan
promotion and a 2001-node tree.

### 3B.6 Defects found

| # | Defect | How found | Fix |
|---|---|---|---|
| 1 | `BuildPath` was internal, so the prefix-safety property could not be asserted | Test compilation | `InternalsVisibleTo`, with the reason recorded in the csproj — that property is about ids sharing a prefix and cannot be shown through the public API |
| 2 | `ComplexProperty` nullability mismatch | Compiler | Signature aligned with EF's |
| 3 | Test used single-character unit codes, which the 2-character minimum correctly rejects | Test failure | Test data corrected. The rule was right; the test was wrong. |

### 3B.7 Not built

- `EmployeeAssignment` history — deferred (P1); current unit, position and
  manager live on the employee.
- Matrix reporting — one manager, because workflow's approver resolution must be
  unambiguous.
- Custom attribute extension bag — planned in ARCHITECTURE.md §7.2.2, not built.
- Position and company endpoints — entities and repository exist; endpoints do
  not.
- Integration tests for Organization.

---

## 3C. Phase 4 report (Authorization)

### 3C.1 What this phase closed

**Nineteen endpoints across Identity and Organization had declared permissions
that nothing evaluated.** Any authenticated user could administer every user and
restructure the whole organization. That is now enforced.

### 3C.2 What was built

**Domain** — `PermissionName` (the `<application>.<resource>.<action>` shape and
its namespace rule), `RegisteredApplication`, `Permission`, `Role`,
`RolePermission`, `UserRoleAssignment`, `GrantedScope`, `AccessDecision`,
`EffectivePermissions`, eight integration events.

**Application** — `PermissionResolver` with a version-stamped cache,
`GrantRoleHandler` and `RevokeRoleHandler` with the anti-escalation rules,
`DeclarePermissionsHandler` for application manifests.

**Api** — `PermissionPolicyProvider` (builds a policy per permission on demand),
`PermissionAuthorizationHandler` (the enforcement), seven endpoints.

**Infrastructure** — `authz` schema, repository with the permission join as one
query, `PermissionVersionStore`, `EffectivePermissionCache`,
`OrganizationScopeReader`, and the seeder.

**Kernel** — `RequirePermissionAttribute` now implements `IAuthorizeData`, and a
neutral `ScopeFilter` that any module's endpoint can apply.

### 3C.3 The decisions that mattered

**A decision returns a filter, not a boolean.** An authorization system that
answers only yes or no relies on every caller remembering to restrict afterwards,
and eventually one will not — returning the whole company to someone entitled to
one department. The filter is now applied at the data layer for employee search.

**The version stamp lives in the database, not in memory.** A TTL cache leaves a
window in which someone keeps access that was deliberately revoked, and "however
short" is not a property anyone can reason about during an incident. An in-memory
counter would let one instance keep serving access another revoked. One indexed
row read per request buys exact correctness: **revocation takes effect on the
very next request.**

**The Platform's own permissions are derived from endpoint metadata**, not from a
hand-maintained list. A list would be correct the day it was written and wrong
within a month.

**Policies are built on demand.** A business application declares permissions the
Platform has never heard of, so the set is only known at runtime. That is what
lets `finance.invoices.approve` work with no Platform code change.

**A holder-relative scope on a caller with no employee record grants nothing.**
Treating it as `All` would be catastrophic; as `Self`, silently wider than
granted. Denying is the only safe default.

### 3C.4 Anti-escalation

| Rule | Without it |
|---|---|
| No user may grant a permission they do not hold | Anyone with `roles.assign` hands themselves everything via a second account |
| No user may grant a scope wider than their own | Department access grants company-wide access |
| No user may grant or revoke their own roles | Anyone reaching the endpoint escalates; anyone under investigation tidies their access away |

### 3C.5 Tests

| Suite | Tests | Result |
|---|---|---|
| `CCP.Kernel.UnitTests` | 53 | ✅ |
| `CCP.Architecture.Tests` | 20 | ✅ |
| `CCP.Modules.Identity.UnitTests` | 116 | ✅ |
| `CCP.Modules.Organization.UnitTests` | 48 | ✅ |
| `CCP.Modules.Authorization.UnitTests` | 63 | ✅ |
| **Executed total** | **300** | **All passing** |

Authorization coverage: permission evaluation in every scope combination
including the widest-wins rule and the no-unit denial; prefix safety across
sibling paths; namespace ownership including the reserved `platform` namespace;
the three anti-escalation rules; grant expiry and revocation; the policy provider
and the enforcement handler, including that 401 and 403 are distinguished and
that the filter travels.

### 3C.6 Defects found

| # | Defect | How found | Fix |
|---|---|---|---|
| 1 | `TryGet` did not state its nullability contract, so the resolver dereferenced a possibly-null cache entry | Compiler | `[NotNullWhen(true)]` |
| 2 | Scope filter could not cross module boundaries without Identity and Organization referencing Authorization's internals | Noticed while wiring the filter to an endpoint | A neutral `ScopeFilter` in the kernel, like the correlation id — every module consumes it, none owns it |
| 3 | `StartsWith` inside an EF expression is culture-dependent and the `StringComparison` overload is untranslatable | Analyzer | `EF.Functions.Like`, which states exactly the SQL the `text_pattern_ops` index serves |

Defect 2 is the interesting one: it was only visible once the filter had to be
*applied* rather than merely *returned*.

### 3C.7 Not built

- **Role and application management endpoints.** Roles can be read and granted;
  creating one is currently a seeding concern. The application registry has a
  domain, repository and declaration handler but no endpoints — those are
  Phase 11 with client credentials.
- **The user list applies no scope filter.** Identity has no organizational
  dimension of its own, so what `Unit` scope means there is a decision, not an
  oversight. Most likely "users linked to employees in reachable units".
- **Machine-to-machine credentials** — Phase 11.
- **Integration tests** — nothing has run against a real database.

---

## 3D. Phase 5 report (Security Hardening)

### 3D.1 What this phase closed

**`/auth/login`, `/auth/password/forgot` and `/auth/password/reset` were
anonymous and effectively unlimited.** The global limiter allowed 600 requests a
minute — a number chosen for browsing, which for password guessing is an
afternoon's work. That was the largest remaining gap in the Platform, and it is
now 10 a minute.

**MFA existed only as an ADR.** Administrative permissions could be exercised by
anyone holding a valid token, with nothing between a stolen session and a role
grant. That is now step-up authentication.

### 3D.2 What was built

**Rate limiting** — four sliding-window policies (`Authentication` 10/min,
`Anonymous` 60/min, `Write` 120/min, `Read` 600/min), applied per endpoint, with
the global limiter kept as a backstop for anything that declares none. Sliding
rather than fixed, because a fixed window permits a double burst at the boundary
— exactly where an attacker aims.

**TOTP (RFC 6238)** — implemented directly rather than taken from a package:
thirty lines, verified against the RFC test vectors, and no third-party
dependency in the authentication path. Constant-time comparison, and the drift
loop deliberately does not break on a match so that the matching period cannot be
timed.

**MFA enrolment** — pending until proven, secret returned exactly once, ten
recovery codes from a confusable-free alphabet, single-use and invalidated with
the factor. One enrolment per user, enforced by a unique index rather than by
convention.

**Secret protection** — AES-256-GCM with a fresh nonce per encryption, stored as
`nonce | tag | ciphertext`. The key arrives as a **path** to a mounted file, never
as a value. Outside Development the Platform refuses to start without it.

**Step-up authentication** — `[RequireStepUp]` on six privileged endpoints, with
elevation held in the database, bound to the session, absolute at fifteen
minutes, and revoked when MFA is disabled.

**Security event log** — `security.security_events` with four composite indexes,
no foreign key to `identity.users`, and a search endpoint bounded to a default
seven-day window.

### 3D.3 The decision that shaped the phase

**MFA is enforced at the privileged action, not at sign-in.**

The obvious design is to refuse sign-in to an administrator without a second
factor. It is also wrong twice over: it locks people out of the very system they
must use in order to enrol one, and it punishes people for a policy change made
while they were away.

Enforcing at the action instead does something the door cannot: it catches a
session stolen *after* a legitimate sign-in. The token is valid, the session is
real, and step-up still stops it. An administrator with no MFA can sign in and
read; they cannot act.

The cost is that the set of protected endpoints becomes a security decision
someone has to maintain. `StepUpCoverageTests` pins it **in both directions** —
losing a requirement fails the build, and so does gaining an unreviewed one. A
test checking only a minimum would let elevation spread until people worked
around it.

### 3D.4 Boundary held

The step-up attribute and its requirement live in the **kernel**; the policy is
built by the **Authorization** module's provider; the state is read by a handler
in the **Security** module. That split is what lets an endpoint in Identity demand
step-up without Identity referencing Security (§6.2). No module gained a
reference to another this phase.

### 3D.5 Tests

| Suite | Tests | Result |
|---|---|---|
| `CCP.Kernel.UnitTests` | 53 | ✅ |
| `CCP.Architecture.Tests` | 25 | ✅ |
| `CCP.Modules.Identity.UnitTests` | 116 | ✅ |
| `CCP.Modules.Organization.UnitTests` | 48 | ✅ |
| `CCP.Modules.Authorization.UnitTests` | 63 | ✅ |
| `CCP.Modules.Security.UnitTests` | 56 | ✅ |
| **Executed total** | **361** | **All passing** |
| `CCP.Api.IntegrationTests` | 52 written | ⚠️ **Never executed** (§4) |

Security coverage: TOTP against the RFC 6238 vectors and base32 against RFC 4648;
the enrolment lifecycle including that a pending factor does not count and that
enrolment failures do not pre-exhaust the attempt budget; recovery code
single-use, retirement on regeneration, and invalidation on disable; AES-GCM
round-trip, nonce freshness, tamper detection and wrong-key rejection; step-up
validity, absolute expiry and revocation.

### 3D.6 Defects found

| # | Defect | How found | Fix |
|---|---|---|---|
| 1 | **The integration test factory migrated only `KernelDbContext`.** The `identity`, `organization` and `authorization` schemas were never created, so every module integration test written since Phase 2 would have failed on a missing table at its first CI run. | Tracing where the new `security` schema would get created | Every module's migrations are now applied, one context per schema, kernel first. |
| 2 | **`SecurityEventRecorder` took the shared scoped `DbContext`**, so its `SaveChangesAsync` would have committed everything else pending — the opposite of the independence its own doc comment claimed, and a way to commit a half-finished operation. | Reviewing the registration against the rationale written in the class | Registered `AddDbContextFactory` and gave the recorder its own context. |
| 3 | **An invalid MFA code returned 401.** A BFF that treats 401 as "the access token expired" would refresh, retry, fail again and sign the user out — turning one mistyped digit into a logout. | Checking the error type against the status code the integration tests asserted | `InvalidCode`, `InvalidRecoveryCode` and `MfaLockedOut` are rule violations (400). The status code has to distinguish "your session is no good" from "that code is no good", because clients act on the difference automatically. |
| 4 | Ambiguous `TryGetIdentity` overloads made `out _` uncompilable | Compiler | Removed the unused overload |
| 5 | `MfaRequiredByPolicy` documented enforcement at sign-in, which is not what was built | Writing the documentation | Comment corrected to describe action-time enforcement |

Defects 1 and 3 are the ones worth dwelling on. **Defect 1 had been latent for
three phases** and was invisible precisely because nothing has ever run against a
database — the blocker in §4 is not only delaying verification, it is hiding
defects. **Defect 3** is a reminder that a status code is an instruction to
automated clients, not a label for humans.

### 3D.7 Not built

- **Administrator-initiated MFA reset** for a user who has lost both phone and
  recovery codes. Needs an audited, permission-gated flow; deferred with the
  account-recovery work.
- **MFA protection key rotation.** Rotating it today would make every enrolled
  secret undecryptable — the stored format has no key version field, which is
  the change that must come first. Recorded as debt, not solved.
- **Argon2id parameter measurement on real hardware.** The parameters are
  defensible defaults, not measured ones.
- **A key-rotation rehearsal in staging.** No staging environment exists.
- **`threat-model.md` and `password-policy.md`** — still outstanding from Phase 2.
- **WebAuthn.** TOTP is not phishing-resistant; this is accepted for now and
  recorded in `docs/security/mfa.md`.

---

## 3E. Phase 6 report (Audit)

### 3E.1 What this phase built

An append-only, partitioned, company-wide trail — and, between phases, the
first deployment: **the Platform is live on Railway**, and the integration
suite ran against real PostgreSQL for the first time.

### 3E.2 What was built

**The event** — the full field set of §15.2. `occurred_at` is the time the thing
happened, not the time it was written, and it is the partition key: audit writes
land a moment later, and recording the write time would misorder events under
load and file one in the wrong month at a boundary.

**Immutability, twice over.** `IAuditRepository` offers append and search and
nothing else; `AuditEvent` exposes no mutation. And the migration revokes
everything on the `audit` schema from the application role, granting back
`INSERT` and `SELECT` only — including on partitions created later, through
`ALTER DEFAULT PRIVILEGES`. Code discipline without the privilege is a promise
that lasts until someone opens `psql`; the privilege without the discipline is an
accident waiting to be made.

**Monthly range partitioning**, with a maintenance job creating three months
ahead and one behind, on startup and daily. A partitioned table *rejects* a row
with no partition, and the first minute of a new month is the worst time to find
that out. A `DEFAULT` partition means a late job costs a misfiled row rather than
a lost event — and rows appearing there are the signal that maintenance is
behind.

**Redaction before storage**, denying by field name rather than by value:
recognising a secret by looking at it is guesswork, but a field called `password`
holds one whatever it contains. Names match as case-insensitive substrings, and a
sensitive name redacts the whole subtree — an object called `credentials` holds
nothing worth keeping, and redacting it field by field would preserve exactly the
structure an attacker wants.

**Ingestion**, internal and external. The application is taken from the caller's
identity and never from the request body: a caller able to name its own
application could write entries attributed to Finance or HR, and a trail anyone
can forge is evidence of nothing.

**Bounded search.** The date range is required — not defaulted — and capped at 90
days. A caller who omitted the dates would otherwise believe they had searched
everything, which in an investigation is worse than an error.

### 3E.3 The decision that shaped the phase

**Audit and the security event log stay separate**, though they were built in
consecutive phases and overlap in shape.

Audit answers *who changed what*; security events answer *what is being
attempted*. Different readers, different retention, different urgency. Merging
them would mean either keeping attack noise for seven years or discarding
evidence after ninety days.

### 3E.4 Tests

| Suite | Tests | Result |
|---|---|---|
| `CCP.Kernel.UnitTests` | 63 | ✅ |
| `CCP.Architecture.Tests` | 25 | ✅ |
| `CCP.Modules.Identity.UnitTests` | 116 | ✅ |
| `CCP.Modules.Organization.UnitTests` | 48 | ✅ |
| `CCP.Modules.Authorization.UnitTests` | 63 | ✅ |
| `CCP.Modules.Security.UnitTests` | 56 | ✅ |
| `CCP.Modules.Audit.UnitTests` | 50 | ✅ |
| **Executed total** | **421** | **All passing** |
| `CCP.Api.IntegrationTests` | 62 | ✅ **Now running in CI** |

Audit coverage concentrates on redaction, which is the most consequential thing
the module does: the table is append-only, so a secret written into it stays
there — there is no update path to remove it and, by design, no privilege to try.

### 3E.5 What the first deployment found

Nine latent defects, none of which could have surfaced locally. Five were in the
build pipeline and had been hiding one another behind a single sentence carried
since Phase 1 — "CI builds and scans the image on the first push" — which was
impossible for five consecutive reasons.

| # | Defect | Age | Would have struck |
|---|---|---|---|
| 1 | Test factory migrated only the kernel schema | 3 phases | Tests |
| 2 | The connection string never reached the host | 5 phases | Tests |
| 3 | `SingleAsync` without filtering by user | 1 phase | Tests |
| 4 | `[..40]` on a 38-character string | 3 phases | Tests |
| 5 | **`Retry-After` absent from every 429** | 1 phase | **Production** |
| 6 | **IP-partitioned rate limit collapses behind NAT** | 1 phase | **Production, day one** |
| 7 | `trivy-action` pinned to a version that never existed | 5 phases | Pipeline |
| 8 | `.gitignore` swallowed `build/`, so the Dockerfile was never committed | 5 phases | Pipeline |
| 9 | Dockerfile copied 5 of 20 projects; `.editorconfig` absent; image never loaded for scanning | 5 phases | Pipeline |

**The lesson worth keeping: a promise that a tool will verify something later is
not verification.** Five phases of local testing did not find what one push found
in an afternoon.

### 3E.6 Closing the retrofit

The phase first shipped with the trail built and **empty**: the module existed,
the seam existed, and no handler called it. Task 10 closed that.

`IAuditTrail` lives in **the kernel**, not in the Audit module. Identity,
Organization, Authorization and Security all have to record, and no module may
reference another (§6.2) — so the contract belongs to the kernel and the
behaviour to Audit, the same split as the neutral `ScopeFilter` and the step-up
requirement. An architecture test asserts the seam stays there and that no module
takes a direct reference on Audit.

`ICurrentUser` was declared in Phase 1 and had never been implemented or used by
anything. Audit needed an actor to attribute events to, so it exists now, reading
only from the validated token: an actor a caller could assert is an actor a
caller could forge, and a trail whose actor field is chosen by the person being
audited is worse than none, because it carries the authority of a record while
being fiction.

The entry a module supplies is deliberately incomplete — module, action, resource
and what changed. Who, from where, and under which correlation id are filled in
from the ambient request. A module that had to pass the actor on every call would
eventually pass the wrong one.

### 3E.7 Not built

- **Asynchronous signed export** (task 7). Until it exists the 90-day search cap
  has no escape hatch for a wider investigation.
- **Retention and archival by partition detach** (task 9).
- **The outbox consumer** (task 3). Events are written directly rather than
  riding along with the transaction that produced them.
- **Search against 10 million seeded rows.** The 2-second budget is unmeasured.

---

## 4. Blockers

| # | Blocker | Severity | Blocks | Needed from |
|---|---|---|---|---|
| B1 | **No database reachable on this machine** | **High** | Running the 27 integration tests | See below |
| B2 | ~~.NET 10 SDK not installed~~ | — | — | ✅ **Resolved** — 10.0.400 installed |
| B3 | **Requirements document still not provided** | High | Confidence in all phases | Project owner |
| B4 | Cloud provider not chosen (Q4) | Medium | Phase 19 | Project owner |
| B5 | ~~Git repository not initialized~~ | — | — | ✅ **Resolved** — initialized on `main` |

### B1 — detail and resolution

Two independent problems, neither in the code:

1. **Docker Desktop cannot start its Linux engine.** WSL2 has no distribution
   installed, so the engine returns HTTP 500 and `docker compose up` fails.
   Fix: run `wsl --install` (requires a restart), then start Docker Desktop.
   This is the documented path and restores `docker compose up -d`.

2. **PostgreSQL 17 was installed natively as a fallback and is running, but its
   superuser password is unknown** — the installer did not apply the password
   passed to it. Resolving this needs either the password to be set through
   pgAdmin / the installer, or Docker to be fixed. I did not attempt to guess
   the password or weaken `pg_hba.conf` to bypass authentication.

Either fix unblocks the integration tests. The suite is written and compiles;
running it is a single command once a database is reachable:

```bash
dotnet test tests/CCP.Api.IntegrationTests
```

**CI is unaffected** — the pipeline provisions PostgreSQL as a service
container, so the integration tests run there regardless of local state.

---

## 5. Open questions

Twelve are recorded in [ARCHITECTURE.md §27](ARCHITECTURE.md). Needed soonest:

| Q | Question | Needed by |
|---|---|---|
| Q1 | Does the requirements document exist? | **Overdue** |
| Q3 | Expected scale (users, apps, audit volume)? | Phase 2 sizing |
| Q5 | Is external SSO required? | **Before Phase 2** |
| Q11 | Existing user/employee data to migrate? | **Before Phase 2** |
| Q10 | Bootstrap administrator: who and how? | Before Phase 4 |
| Q2 | One company, or several legal entities? | Before Phase 3 |
| Q12 | Arabic typeface and brand blue? | Before Phase 7 |
| Q4, Q6 | Cloud provider and data residency | Before Phase 19 |
| Q7, Q8 | RPO/RTO and audit retention | Before Phase 17 |
| Q9 | SMS in the first release? | Before Phase 9 |

---

## 6. Risk register

| ID | Risk | Impact | Likelihood | Status | Note |
|---|---|---|---|---|---|
| R1 | Boundary erosion — business logic pushed into the Platform | High | High | Open | No violation yet; Phase 1 contains no business concept |
| R2 | The missing requirements document changes scope | High | Medium | **Open, ageing** | Phase 2 designs identity; late requirements there are expensive |
| R3 | Over-engineering | Medium | Medium | Open | Held so far: no mediator, no job framework, no broker, no Kubernetes |
| R4 | An authentication or authorization flaw | Critical | Low | **Open — now live** | Authentication is implemented. Mitigated by 103 unit tests covering the security properties explicitly, and by two silent defects being found and fixed. Not yet exercised against a real database, nor penetration-tested. |
| R5 | UI drifts to an AI-dashboard look | High | Medium | Open | Not yet applicable |
| R6 | RTL treated as an afterthought | High | Medium | Open | Not yet applicable |
| R7 | Backups never actually restored | Critical | Medium | Open | Phase 17 |
| R8 | Secrets committed | Critical | Low | **Mitigated** | `.gitignore` written before the first file; CI secret scanning configured; no secret in the repository |
| R9 | Audit becomes a bottleneck | High | Medium | Open | Outbox in place and designed for it |
| R10 | Documentation drifts from implementation | Medium | High | **Mitigated so far** | Docs updated within this phase |
| R11 | Scope creep from future business systems | High | High | Open | |
| R12 | Team unfamiliar with parts of the stack | Medium | Unknown | Open | |
| **R13** | **Local environment cannot run integration tests** | Medium | — | **New, open** | See B1. CI is unaffected. |

---

## 7. Technical debt

| # | Item | Severity | Plan |
|---|---|---|---|
| 1 | ~~Integration tests written but never executed~~ | — | ✅ **Resolved.** 62 run on every push against PostgreSQL in CI, and the Platform is deployed. |
| 1b | (was) 52 integration tests never executed | — | Was Medium. Raised because Phase 5 found a latent defect (the test factory migrated only the kernel schema) that had been invisible for three phases — the blocker is not only delaying verification, it is hiding defects. |
| 2 | ~~`build/api.Dockerfile` never built~~ | — | ✅ **Resolved by evidence rather than by work.** CI builds the image on every run and scans it, and has done since the first push. The entry was written when it had not yet happened and was never revisited. |
| 3 | `RequirePermissionAttribute` declares intent but does not enforce | Low | By design — the handler arrives with Authorization in Phase 4. No endpoint needing enforcement exists yet. |
| 4 | ~~Outbox cleanup job for old processed rows not written~~ | — | ✅ **Resolved**, and it was the oldest open item in the register. Delivered rows are kept seven days and removed in bounded batches, so the first pass on a table nobody has ever pruned drains over hours rather than in one long transaction on the busiest table in the database. Dead-lettered rows are never touched. The sweep reports to the job history like every other. |
| 5 | ~~Identity integration tests written but **never run**~~ | — | ✅ **Resolved by evidence.** They run in CI on every push, inside the 180-odd integration tests that gate the build. The entry contradicted itself — "they compile and run in CI; they have never executed anywhere" — which is what a register entry looks like when it is written once and never read again. What remains true is that they have never run on a development machine here, which is #70 and is about this laptop rather than about the tests. |
| 6 | Breach screening implemented but **disabled by default** | Medium | The k-anonymity checker is written and registered. Turning it on is the owner's call, since it makes an outbound third-party call. While off, no screening happens. |
| 7 | ~~No JWKS endpoint~~ | — | ✅ **Resolved** — `/api/v1/.well-known/jwks.json` publishes public parameters only, with a test asserting no private component can appear. |
| 8 | ~~Rate limiting is global, not per-endpoint~~ | — | ✅ **Resolved in Phase 5.** Four sliding-window classes applied per endpoint; authentication endpoints are 10/min. The global limiter remains as a backstop. |
| 9 | ~~Permission attributes declared but not enforced~~ | — | ✅ **Resolved in Phase 4.** Enforced by the policy provider and permission handler, with the scope filter applied at the data layer for employee search. |
| 15 | ~~The user list endpoint applies no scope filter~~ | — | ✅ **Resolved, and the decision it was waiting on is recorded.** An account has no department; the person behind it does, through an employee record — so a department-scoped caller sees the accounts of the people in their department, and Identity never grows a unit column of its own, which would be a second place the answer lives and a second place it goes stale. **An account with no employee record is invisible to a scoped caller**: it has no place in the organization, so it is in no department, and the alternative would show every service account to every unit administrator in the company. The cost is stated rather than hidden — a unit-scoped administrator who creates an account cannot see it until it is linked to an employee, which is the act that places it. **No reachable units means nobody, not everybody**: treating an empty prefix list as "no filter" is the natural mistake and turns a scope that grants nothing into a scope that grants the lot, so it has a test of its own that was checked against a deliberately broken implementation. The filter is applied **before paging**, because narrowing a page afterwards gives a short page, a wrong total and page numbers describing rows the caller may not know exist. A list of ids rather than a join, since no foreign key crosses a schema boundary: Identity declares `IUserPlacement` and the infrastructure layer answers it through Organization's contract. 3 unit tests, 6 integration tests. `docs/authorization/model.md` §11.1. |
| 16 | ~~Authorization integration tests not written~~ | — | ✅ **Resolved.** The join returns what the evaluator was given, a subject with no grants resolves to nothing rather than to everything — which is what a join written with the wrong kind of outer join would produce, invisibly — a revocation takes effect on the *very next* resolution, and twelve concurrent bumps of the version stamp all count. That last is the one only a database can answer: read-modify-write would collapse two simultaneous revocations into one increment, leaving a stamp that a cache computed before either of them still matches, so one revocation silently does not happen. |
| 17 | ~~Role management endpoints missing~~ | — | ✅ **Resolved.** Roles can be created, renamed, filled with permissions and deactivated, behind `platform.roles.manage` and with the anti-escalation rule applied at definition as well as at grant. Application registration still arrives with client credentials in Phase 11. Until this existed the Platform had one role — the seeder's, holding everything — so granting anybody anything made them a full administrator. |
| 10 | ~~Password reset cannot complete end to end~~ | — | ✅ **Resolved in Phase 9**, and it was the oldest debt in the project. The token had been issued and staged on the outbox since Phase 2 with nothing to deliver it — the flow existed, was tested, and could not complete. Sent by email only: an in-app copy would put a working account-takeover credential in the inbox of the account it takes over. |
| 11 | ~~No recovery path if the last administrator is lost~~ | — | ✅ **Resolved, and deliberately not with a recovery path.** A recovery path is a way in, and a way in is a way in for whoever finds it. Four operations are now refused when they would be the one that strands the Platform: disabling the last account holding `platform.roles.assign`, revoking the last live grant carrying it, emptying the last role that carries it, and switching that role off — the last because a permission carried by an inactive role is not carried at all, which is the case most easily missed. **Asked of the grants, not of the people**: one person can hold the permission through two roles, so revoking one of their assignments is safe while revoking the other is not, and a count of administrators cannot tell those apart. Identity does not learn what a role is to do this — it asks the kernel through `IAdministratorSafety` and Authorization answers, the same seam shape as `IAuditTrail`. If nobody can grant anything already, nothing is refused: the Platform is stranded and a guard firing after the damage is noise. 5 unit tests on the decision and 5 integration tests on the join — the join is the part that can be subtly wrong, because a revoked grant, an expired grant and a grant of a switched-off role all still exist as rows and every one of them would make the guard permit the operation that strands the Platform. `docs/identity/bootstrap-administrator.md` §7. |
| 12 | ~~Organization integration tests not written~~ | — | ✅ **Resolved.** A subtree move rebasing a grandchild that was never named in the command, a refused cycle leaving the tree byte-for-byte as it was, the unique constraint enforced by PostgreSQL rather than by the handler that checks first, the same code allowed in another company — which a unique index on `code` alone would have broken while passing the previous test — and the `text_pattern_ops` index actually serving the prefix query. That last one is not a correctness question until the company has four thousand units, at which point every request that resolves an organizational scope starts scanning the table and nothing anywhere reports an error. |
| 13 | ~~Company endpoint missing~~ | **Was High, not Low** | ✅ **Resolved.** The company can be established, read and renamed. This was recorded as Low and was in fact a deadlock: every read in the module resolves the company first and answered 404 without one, so the whole section reported failure on a working installation — and no endpoint created a company, while `CreateUnit` required one. Reads now treat an empty Platform as empty; writes still refuse. Position endpoints remain outstanding. |
| 18 | ~~MFA protection key cannot be rotated~~ | — | ✅ **Resolved.** The stored form is now `v2.{keyId}.{base64(nonce | tag | ciphertext)}`, and the version lives in a **prefix rather than in the encrypted bytes** — which is what made the change deployable at all: a value written before versioning has no prefix, is recognised by its absence, and is read with the active key, the only key it can have been written with. Prepending a version byte to the plaintext form would have caused exactly the outage this exists to avoid. Retired keys are configured for reading while the active key encrypts; a secret naming a key the deployment does not hold is **denied rather than guessed at**, since trying every key would turn a configuration mistake into a silent success under the wrong assumption. A key id containing the field separator is refused at startup rather than discovered on the day of a rotation. Procedure in `docs/security/secrets-management.md` §5.1. 7 unit tests, the most important being the dullest: a secret written by the previous implementation still reads. (see #77) |
| 77 | ~~No automatic re-encryption after an MFA key rotation~~ | — | ✅ **Resolved.** A successful TOTP verification now rewrites a secret found under a retired key — or under no named key at all, which is everything stored before versioning existed — under the active key, saved with the rest of that verification. **The entry this replaces claimed it "needs the verification path to be able to save, which it currently cannot"; that was wrong.** `VerifyMfaHandler` already called `RecordSuccess` and issued a step-up confirmation, so it saved on every success; the work was one interface member and four lines, not a restructuring. Two things it deliberately does not do: a **recovery code** never decrypts the secret and so never moves it (a rotation is not finished merely because everybody has verified *something*), and a **disabled enrolment** is left alone rather than carried forward onto every future key. It is still not a forcing pass — a dormant account sits on the old key indefinitely — so `docs/security/secrets-management.md` §5.2 gives the query that says when the retired key can actually come out, rather than a guess about how long is long enough. (closes the follow-up to #18) |
| 19 | ~~No administrator-initiated MFA reset~~ | — | ✅ **Resolved.** Losing both the phone and the recovery codes left an account permanently unusable — not a security property, a locked door with the key inside. `POST /security/users/{id}/mfa/reset`, behind `platform.security.manage` **and step-up**: a stolen session must not be usable to disarm everybody else, and the person removing a factor should have one. Deliberately a separate operation from the self-service disable, with its own audit action and its own security event type — one is somebody managing their own account, the other is somebody else's protection being taken away, and a shared record could not tell an investigation which had happened. Resetting one's own factor here is **forbidden**, since that would be the self-service path with its proof removed. Two architecture tests refused the change and both were right: the reviewed step-up set now records the decision, and the rule that MFA endpoints must not require step-up was narrowed to `/me/mfa` — its reason is the enrolment deadlock, which only arises on one's own factor — with the converse added, so an MFA endpoint acting on somebody else must carry both a permission and step-up. |
| 20 | TOTP is not phishing-resistant | Medium | A convincing fake login page can collect and replay a code within thirty seconds. WebAuthn is the answer and is kept as an extension point. Accepted and recorded, not overlooked. |
| 21 | Rate limits are per-process | Low | A multi-instance deployment multiplies the effective limit by the instance count. Needs shared limiter state if it becomes material. |
| 22 | Argon2id parameters are defaults, not measured | Medium | Carried from Phase 2. Needs measurement on the deployment hardware. |
| 23 | ~~Authentication rate limiting is partitioned by IP, which fails behind NAT~~ | — | ✅ **Resolved.** Authentication is now partitioned by the account being targeted, with a separate per-address limit chained onto the global limiter for the other direction and account lockout as the third layer. `NatRateLimitTests` reproduces an office behind one address. (was:) | Every employee in one office shares one public address and therefore one budget, so 10/min is really "ten sign-ins a minute for the whole company" — the eleventh person arriving on Sunday morning is refused. Surfaced by the integration suite, which trips the limit for exactly this reason: a test process behind one address is a perfect simulation of an office behind one NAT. **Deferred by decision, not oversight.** The fix is to partition the authentication class by the account being attacked as well as by source, keeping a much looser per-IP limit as a backstop; account lockout stays the third defence. Must be resolved before real users. |
| 24 | Rate limits are raised in the integration suite | Low | The suite shares one address, so at the production limit everything after the first ten sign-ins fails with 429. `RateLimitTests` runs at a limit of 3 and asserts the rejection and its `Retry-After` header, so the limiter itself stays covered. |
| 25 | ~~Accessibility not verified with a tool~~ | — | ✅ **Resolved.** axe against WCAG 2.1 AA on every screen in both locales, plus the sign-in page and an open dialog, in CI. It found one real defect: `--color-text-muted` at 3.66:1, the colour of the word "(Required)" beside every field label. A unit test now computes the ratio for every text token. Automated checks find perhaps a third of real barriers, so this is a floor rather than a certificate. |
| 26 | ~~Responsive behaviour not verified~~ | — | ✅ **Resolved.** Four widths, both locales, every screen: no page scrolls sideways anywhere, and both halves of the table rule are asserted — stacked below the small breakpoint, a real table above it. |
| 27 | ~~A role's permissions are replaced blind of concurrent edits~~ | — | ✅ **Resolved.** PostgreSQL's `xmin` as an optimistic concurrency token — every table already has it, so no migration and no column of our own to keep correct. The conflict is **refused rather than merged**: the request is the whole desired set, so applying it late would remove whatever the other person just added and nothing in the request says which was right. EF reports this as `DbUpdateConcurrencyException`, which the Application layer cannot see and should not — the unit of work translates it into a kernel-level `ConcurrentChangeException`, and the first attempt at this was refused by the build for importing EF Core to catch it. Recorded as Low, which was generous: nothing failed, nothing was logged, and the person whose change disappeared had no way to find out. |
| 28 | Workflow has no callback for business-conditional routing | Medium | ARCHITECTURE.md §16.3 offers two escapes for routing that depends on business data: the caller supplies assignees at start time, or the engine asks the application through a callback. The first is built; the second is not. Until it is, an application whose next step depends on data the Platform cannot see must resolve it before starting — which is sufficient, and is what the integration guide says. |
| 29 | ~~A workflow instance cannot be reassigned by an administrator~~ | — | ✅ **Resolved.** `POST /workflow/tasks/{id}/reassign`, behind `platform.workflow.manage` — not `start`, because moving somebody else's approval is an administrative act on the engine and somebody who can raise a request should not thereby choose who approves it. **It is deliberately not delegation:** `DelegatedFromUserId` means *this person chose to pass it on*, and setting it would put words in the mouth of somebody who may have left the company. It stays null and the trail records the administrator, both people and a required reason — the one action in the engine where a name is taken off an approval by a third party, so a record naming only the new assignee would lose that anything was taken from anyone. A settled task cannot be moved; an escalated one can, which is the case this exists for. 9 unit tests. |
| 30 | ~~Workflow unit tests not written~~ | — | ✅ **Resolved.** 33 tests on the definition, the state machine and the task: publication refusing a dead end and an unreachable step while allowing a cycle, every terminal action mapping to its own outcome, a return with no target recording nothing, only the assignee acting, delegation moving the task and not the instance, escalation firing once and not reassigning. |
| 31 | ~~Notification listeners are not deduplicated on event id~~ | — | ✅ **Resolved.** A `processed_events` row written in the **same `SaveChanges`** as the notification — the only arrangement that works, since a marker committed separately leaves a window in which the notification exists and the marker does not. Keyed on the event **and the reason**, because two listeners may legitimately react to one event and a key on the event alone would let whichever ran first silence the other for ever, which is precisely the failure this entry warned about. The reason is the template code, not the listener class name: a class can be renamed and a template code cannot. Done in the sender so every listener gets it and none has to remember. **Five sends were changed and four of them actually were** — the fifth had an explicit channel list in the way of the edit, and it was the password reset. Nothing failed and nothing could have, so `NotificationIdempotencyTests` now fails the build on a listener that sends without saying which event it is answering. |
| 32 | ~~Per-user language is not stored~~ | — | ✅ **Resolved.** A nullable `preferred_locale` on the account, chosen through `PUT /me/language`, read by the recipient directory. **Null is a real answer, not a missing one:** it means the person never chose, so the company default applies *and keeps applying if the company later changes it* — storing the default at sign-up instead would freeze every account on whatever the company spoke the day it was created, and nobody would ever work out why changing the company language changed nothing. A closed list of `ar`/`en` rather than a tag pattern: `fr` is well-formed and the Platform has no messages in it, so accepting it would store a preference that silently falls back for ever, which reads as the choice being ignored. The portal's language switcher records the choice with a `keepalive` request, so the link still behaves as a link and a failure to store never stops somebody changing the language they are reading in. 7 unit tests. |
| 33 | The email provider is a direct SMTP adapter | Low | Planned: outbound calls get a governed path in Phase 12 (§17.2, §19.1). Until then this talks to a mail server directly, with no shared circuit breaker or outbound policy. |
| 34 | ~~No stub provider demonstrates the third channel~~ | — | ✅ **Resolved.** An integration test registers a third provider into the real host and asserts the dispatcher resolves it — one class, one registration, nothing else changed. Written as a test rather than a paragraph because it stops being true the moment somebody adds a switch on channel type, and a paragraph would not notice. |
| 35 | ~~No orphan-object reconciliation job~~ | — | ✅ **Resolved.** `documents.reconcile` runs daily, streams the store, checks each key against `documents.versions` in batches of 500, and reports the count and the bytes in the job history. **It never deletes, and that is the design rather than caution.** An orphan is defined by the database never having heard of it — which is also exactly what every object looks like when the database is not the one that wrote the bucket: a restored backup, a connection string pointed at the wrong environment, a staging deployment sharing production storage. A sweep with delete rights removes every document uploaded since, permanently. The purge sweep destroys content because it acts on a record that says to; this has an absence, and an absence is not an instruction. **Objects written in the last hour are not judged at all** — between the store and the commit a perfectly good document is content with no row, indistinguishable from an orphan by every measure except age, and that count is reported separately so a pass that judged nothing does not read as a pass that found nothing. The storage seam gained `ListAsync`, streamed rather than materialised (a bucket has no upper bound), implemented for both providers — the S3 one pages on the continuation token, because a listing that stopped at the first page would report a bucket of a million objects as a bucket of a thousand and call the rest accounted for. 6 unit tests; the grace period and the final partial batch were each checked against a deliberately broken implementation. |
| 36 | ~~The object-storage path is not tested against a real bucket~~ | — | ✅ **Resolved.** CI now runs MinIO — the same image and the same command as `docker-compose.yml`, so what CI exercises is what a developer runs — and 9 integration tests drive the S3 provider against a real bucket it creates and removes. **The assertion the suite exists for: the same address without a signature is refused**, checked alongside a signed fetch that succeeds so it is a test of the signature rather than of a bucket name that was wrong all along. A public bucket would make every access rule in the Documents module decorative, since anybody who learned a key could fetch the file without reaching the Platform — and the key is in the URL of every download it has ever issued. Also covered, and uncheckable against the local provider: that a pre-signed URL **expires**, that it carries the file name and forces a download, that a name carrying a quotation mark cannot break out of the header, and that a missing object reads as nothing rather than throwing. A step rather than a service container, because MinIO needs its `server /data` arguments and GitHub's services syntax has nowhere to put them. |
| 37 | A ZIP is accepted on the strength of its extension | Low | The bytes prove it is an archive; which member of the ZIP family it is comes from the file name, because telling a `.docx` from an `.xlsx` means opening the archive. The security question is answered by the content and the cosmetic one by the name, so the worst outcome is a spreadsheet labelled as a document. |
| 38 | A unit access rule is evaluated against the caller's unit, not the document's | Low | Access is decided by rules alone; a document sitting in a unit grants nobody anything by virtue of sitting there. That is deliberate — the alternative hands a department head every private letter written to anybody who reports to them — but it does mean `organizationUnitId` on a document is a filter and not a permission, which is easy to misread. |
| 39 | ~~Webhook subscriptions are not built~~ | — | ✅ **Resolved.** `POST /integrations/subscriptions` registers a business application's standing request to be told when something happens; a fan-out turns every Platform event into one queued delivery per interested subscriber; a sweep posts them with exponential backoff and jitter, six attempts over roughly an hour. **Every delivery goes out through the same door as every other outbound call** — allow-list, private-address check, and the guarded socket that connects to the address it checked — which is exactly why this waited for the phase that owns outbound calls: a subscription nobody governs is an SSRF primitive. The address is checked **when the subscription is registered**, because a subscription nobody can deliver to is one whose owner believes they are being told things. Signed with the same `HMAC-SHA256(secret, "{timestamp}.{body}")` scheme the Platform demands of inbound webhooks — one implementation to get right — and **never sent unsigned**: an unresolvable secret fails the delivery rather than teaching receivers to accept unsigned messages. There is deliberately **no way to subscribe to everything**; a subscription that did would receive events added years later and find out by failing to parse them. The kernel gained one neutral seam, `IIntegrationEventObserver`, because which events matter is chosen by an application at runtime and no typed subscription could express it. The fan-out **queues and does not send**: the outbox relay is holding a transaction, and posting from inside it would stall the relay for everybody behind one slow endpoint. Abandoned deliveries are kept, because "we tried six times and your endpoint refused every one" is the question a subscriber eventually asks. A subscription suspends after 20 consecutive failures — suspended, not deleted, and resuming clears the count, since resuming with the failures still recorded would suspend it again on the next failure and look like it had done nothing. 18 unit tests. `docs/integrations/README.md` §8. **The portal screen is not built yet** and is recorded as its own item. |
| 40 | The integration guide has not been tested on a real outside developer | **Medium, and now the oldest open documentation item** | Phase 11's acceptance criterion says explicitly: validated by having someone actually try, not by self-assessment. Writing it and reading it back is exactly the self-assessment the criterion rules out. Two errors were caught by checking the guide against the generated contract — an endpoint that did not exist and a method that was wrong — which is evidence that reading it back is not enough. |
| 41 | A machine token cannot be revoked before it expires | Low | Revoking a credential stops new tokens instantly, and tokens already issued keep working for the rest of their short lifetime. The alternative is checking a revocation list on every request, which makes the Platform a synchronous dependency of every call in the company — the thing asymmetric signing and the JWKS endpoint exist to avoid. Stated in the documentation rather than implied away. |
| 42 | An application declares its permissions with no permission of its own | Low | Being an authenticated application declaring **its own** namespace is the authorization, and the namespace comes from the token so it cannot be anything else. Declared permissions grant nobody anything until an administrator puts them in a role, so the blast radius is rows in a table — weighed against two administrator actions to onboard every system. |
| 43 | ~~The Platform would not start if it could not create a document folder~~ | — | ✅ **Resolved.** The local storage provider created its root directory in its constructor; the container runs as an unprivileged user and the application directory belongs to root, so the call was refused — and because that provider is a singleton resolved while the host is being built, every module failed to start. The constructor now does no I/O, the default root is a writable temporary directory, and three tests pin all of it. |
| 44 | ~~DNS rebinding is not closed~~ | — | ✅ **Resolved.** The guard now opens every outbound socket itself: it resolves once, approves what it resolved, and connects to **those addresses**. The fix is not a better check but **one lookup instead of two** — the window existed because the guard checked a name and the HTTP client then resolved that name again, and whoever runs its DNS chooses the second answer. The same callback closes a second hole found while building it: **a redirect sends the client to a host the pre-flight check never saw**, and the connection layer is the only place that learns where, so the allow-list is consulted there too. A refusal is logged as `Blocked` rather than `Failed` and is **neither retried nor counted towards opening the provider's circuit** — a policy decision does not become truer on the third attempt. Pooled connections gained a two-minute lifetime, because the default is forever and a connection approved once was kept regardless of what the name resolved to later. The whole policy still hangs off **one seam**: `IOutboundGuard` gained `ApproveAsync`, so the integration suite's substitute guard had to answer for the socket as well — the compiler said so, which is what a single seam is for. The test that matters says yes at the request and no at the socket: a connect callback written but never wired into the handler would look exactly like one that works, and every other test in that suite is allowed by both checks and so could not tell them apart. 7 unit tests, 1 integration test. `docs/integrations/README.md` §4. |
| 45 | The email channel has not been moved onto the integration layer | Medium | Phase 12 lists it, and Phase 9 recorded it as debt (#33). The SMTP adapter still talks to a mail server directly, with no shared circuit breaker, no allow-list and no entry in the call log. SMTP is not HTTP, so it needs a second connector shape rather than a configuration change — which is the reason it is not done rather than an excuse for it. |
| 46 | ~~Outbound webhook subscriptions are not built~~ | — | ✅ **Resolved.** `POST /integrations/subscriptions` registers a business application's standing request to be told when something happens; a fan-out turns every Platform event into one queued delivery per interested subscriber; a sweep posts them with exponential backoff and jitter, six attempts over roughly an hour. **Every delivery goes out through the same door as every other outbound call** — allow-list, private-address check, and the guarded socket that connects to the address it checked — which is exactly why this waited for the phase that owns outbound calls: a subscription nobody governs is an SSRF primitive. The address is checked **when the subscription is registered**, because a subscription nobody can deliver to is one whose owner believes they are being told things. Signed with the same `HMAC-SHA256(secret, "{timestamp}.{body}")` scheme the Platform demands of inbound webhooks — one implementation to get right — and **never sent unsigned**: an unresolvable secret fails the delivery rather than teaching receivers to accept unsigned messages. There is deliberately **no way to subscribe to everything**; a subscription that did would receive events added years later and find out by failing to parse them. The kernel gained one neutral seam, `IIntegrationEventObserver`, because which events matter is chosen by an application at runtime and no typed subscription could express it. The fan-out **queues and does not send**: the outbox relay is holding a transaction, and posting from inside it would stall the relay for everybody behind one slow endpoint. Abandoned deliveries are kept, because "we tried six times and your endpoint refused every one" is the question a subscriber eventually asks. A subscription suspends after 20 consecutive failures — suspended, not deleted, and resuming clears the count, since resuming with the failures still recorded would suspend it again on the next failure and look like it had done nothing. 18 unit tests. `docs/integrations/README.md` §8. **The portal screen is not built yet** and is recorded as its own item. |
| 78 | ~~Outbound subscriptions have no screen~~ | — | ✅ **Resolved, the same day it was recorded.** The Integrations screen gained a subscriptions table: register, reconfigure, switch off, resume, and the delivery history for each. **The event list is listed rather than counted** — which events a system receives is the whole content of a subscription, and a number says nothing anybody needs. A suspended subscription carries its reason on the row rather than a click away, because it is a business system that has silently stopped hearing about anything. The signing secret appears as a **reference and is not a password field**: masking it would teach the person that a value belongs there, which is the opposite of what credential-by-reference is for. The refusal for an address the allow-list does not carry is named in full, since that one is a configuration change rather than a typing mistake. 40 catalogue keys at parity. |
| 47 | ~~No integration administration screens~~ | — | ✅ **Resolved.** Providers, their health, and the call log with both redacted payloads. Enabling and disabling is on the screen; registering a provider and editing its resilience, redaction and credential reference is still an API call (#57). |
| 49 | ~~A provider configured not to retry failed every call~~ | — | ✅ **Resolved.** Polly validates `MaxRetryAttempts` as at least one; the domain allows zero to mean *do not retry*, and the factory passed it through, so the pipeline threw while being built. Zero retries now means no retry strategy. Found by the stub-server tests within an hour of writing them, which is the clearest argument for having written them. |
| 50 | ~~The feature-state endpoint answers without the caller's roles or units~~ | — | ✅ **Resolved.** `GET /me/features/{key}` passed two empty lists to an evaluator that reads them, so every **targeted** flag answered *off* to everybody who asked through the API. The domain's `IsOnFor` was always correct and always covered; the endpoint never gave it anything to work with. Every layer passed its own tests and the feature did not work — a rollout aimed at one department simply never arrived, nothing failed anywhere, and the screen that asked could not tell that from a flag which was genuinely off. Configuration now declares `IFeatureSubjectResolver` in its Application layer and implements it in Infrastructure against the Authorization and Organization contracts, the same seam Documents and Workflow use. Five integration tests, asserted in pairs — on for the target *and* off for a non-target — because the first alone would pass on a build that ignored targeting and answered yes to everybody, which is the worse defect of the two. This unblocks #59. (was:) | `GET /me/features/{key}` resolves neither, so a **targeted** flag reads as off for everybody through it. An undeclared or untargeted flag answers correctly, and the Platform's own code evaluates targeting properly because it already knows the caller. The endpoint needs the same subject resolution the Documents module has; until then a screen cannot be shown a targeted rollout. |
| 51 | ~~No configuration administration screens~~ | — | ✅ **Resolved.** Settings with what is in force and their full change history, and flags with their reach. The history was the point: one that only SQL can read gets read once, during an incident. Editing is limited to Platform scope and flag targeting is not editable (#58). |
| 52 | ~~Nothing has been migrated onto the configuration module yet~~ | — | ✅ **Resolved**, seven phases after the module was finished. `IPlatformSettings` is a neutral kernel seam — the same shape as `IAuditTrail` and `IJobJournal` — so the kernel's outbox and three module sweeps read a changeable value without any of them referencing the Configuration module. Four durations are now settings: outbox retention, job history retention, integration call log retention, and the document deletion grace. Each is read **at the moment it is used**, so a change takes effect on the next pass rather than the next deployment, and each passes the value it shipped with as the fallback — so an unreadable setting behaves exactly as the Platform did before, and never as a retention of zero, which would delete everything. A seeder declares them on startup from those same options, so the number on the screen and the number in the code are equal by construction. The grace period is read at deletion and stamped as an instant, because shortening it must not retroactively destroy something already inside the period it was promised. |
| 53 | ~~The tests about secrets put secret-shaped strings in the repository~~ | — | ✅ **Resolved.** The secret scanner failed the phase whose subject is keeping credentials out of places they do not belong, having found convincing tokens in my own fixtures — which is the scanner working, since it cannot tell a test from a leak. The fixtures are assembled at run time; an allow-list entry would have been a permanent hole opened so a test could keep its formatting. |
| 54 | ~~No stored background job history and no monitoring screen~~ | — | ✅ **Resolved.** A kernel table, a neutral seam, one runner shared by every periodic sweep, and a screen. Building it uncovered #60: the metric these runs were supposed to feed had never been reachable from the code meant to feed it. |
| 55 | Alert definitions are written down and not deployed | Medium | The runbook names seven conditions with thresholds and a first action for each. Creating them is an operation in whichever backend the provider offers, and the provider is not chosen (Q4) — so the definitions exist as documentation and nothing is watching. The Operations screen now answers six of the seven on demand, which is a page somebody opens rather than something that wakes them. |
| 56 | Trace context is not propagated to business applications | Low | The Platform accepts an inbound correlation id and echoes it, and outbound integration calls carry W3C trace headers through the instrumented HTTP client. What is untested is the round trip: a business system's trace joining the Platform's and coming back. It needs a second service to test against. |
| 48 | ~~The connector is not tested against a real HTTP server~~ | — | ✅ **Resolved.** A stub provider on a real socket, misbehaving on command: a retry works through two failures and the stub counts three arrivals, a 400 is not retried and the stub counts one, a timeout fires inside its budget while the stub sleeps five seconds, the breaker opens and the stub stops receiving anything, and a card number is absent from both halves of the stored log while the caller still gets it. The counts come from the far end of the socket rather than from the Platform's own log, so the log is not being tested against itself. |
| 60 | ~~The background-job metric was emitted by nothing~~ | — | ✅ **Resolved, and it is the most instructive defect of the project so far — it was not even the only one.** `PlatformMetrics.BackgroundJobRan` was declared in Phase 14, given an alert in the runbook, and placed in `CCP.Kernel.Api` — which no module's Infrastructure project references. Every one of the five background sweeps lives in a module's Infrastructure. So the method could not be called from the only code that had any reason to call it, nothing failed to compile, nothing was logged, and the alert read green whether the sweeps ran or not. It survived a phase, a review and a green CI run because **nothing anywhere asserts that a declared instrument is emitted**. The meter now lives in the application layer and the sweeps report through `JobRunner`, which has tests. |
| 61 | The outbox relay and the notification dispatcher keep no run history | Low | Deliberate. Both are continuous pollers — five and fifteen seconds — so journalling each pass would write over twenty thousand rows a day and drown the four rows that answer a question. Their health is visible in their own terms instead: outbox depth and the age of the oldest undelivered message on the same screen, and the notification delivery log. If a poller stops, the age climbs, which is the signal that matters. |
| 62 | ~~The job summary reports the newest run's instance, not every instance~~ | — | ✅ **Resolved.** Each job row now carries a per-instance breakdown for the window, plus a `failingInstances` count. **The row is still about the job**, because that is what somebody is looking for at three in the morning — what changed is that its last outcome is no longer a single value hiding a plural fact. Two instances running the same sweep, one failing every pass and one succeeding every pass, produced a fifty-per-cent failure rate and a last outcome that depended on which machine finished most recently; "intermittent" and "one machine is broken" call for entirely different repairs, and the first one wastes the night. The screen prints the breakdown **only where there is more than one instance**, which is what the original entry said had to be decided: on a single-instance deployment a breakdown of one is noise. Scoped to the last day rather than to all time, because an instance name on a container platform changes with every deployment — grouped over all history it would list every container that ever existed and answer nothing. Three integration tests, one of them checking that the breakdown has not become a second way for a stopped job to vanish from the page. |
| 63 | ~~Nothing asserts that a declared instrument is ever emitted~~ | — | ✅ **Resolved, and it found a second one within a minute.** `InstrumentCoverageTests` reflects the recording methods off `PlatformMetrics` and fails the build if any has no caller in `src/`. On its first run it named `ccp.outbox.dispatches` — declared in Phase 14, never wired to the relay it describes, and answering "are events getting out?" with silence. So the class of bug that produced #60 had already produced a second instance nobody had noticed. It cannot prove a call is on a path that runs; it catches the failure that actually happened, twice. |
| 64 | ~~The application connects with one database role for both DDL and DML~~ | Low — **the Platform supports it; a deployment has to adopt it** | 🟡 **Built, not yet adopted anywhere.** The Platform now takes an optional second connection string, `ConnectionStrings:PlatformMigrations`, used by the migrator and by nothing else; `scripts/database-roles.sql` creates `ccp_migrator` and `ccp_app` with the right grants and prints what each ended up holding. **Building it found the half that fails silently.** The audit trail's append-only guarantee is a `REVOKE` issued by a migration, and a migration naming `current_user` names *the migrator* once the roles are apart — so the grant would land on the role that never writes an audit event and the application would be left not append-only but **unable to append**, with nothing saying so until the first audited action in production. The role is therefore told to the migration (`Database:ApplicationRole`, published as the session setting `ccp.application_role`) rather than assumed by it, falling back to `current_user` so every existing single-role environment behaves exactly as before. Two integration tests run the shipped migration's **own SQL, read from the migration object rather than transcribed**, and ask PostgreSQL what the role ended up holding. `DATABASE_URL` is deliberately not a fallback for the migration string — it is the application's credential, and falling back to it would silently reunite the roles in the one deployment that had separated them. What remains is a deployment decision, and the deployment is Q4. `docs/deployment/database-roles.md`. |
| 65 | Nothing is taking a backup | **High** | The runbook is written and the verification script runs, and no backup exists to run it against. Continuous archiving and point-in-time recovery are features of a managed PostgreSQL, and the provider is undecided (Q4) — so this is genuinely blocked rather than deferred. It is recorded as High because every other risk in this register is survivable and this one is not: the audit trail cannot be reconstructed from anywhere. |
| 66 | The restore verification has never been run against a real dump | Medium | It is a shell script with no test of its own, and the local machine has no PostgreSQL superuser password (#1's original cause), so it has been syntax-checked and read rather than executed. Its assertions were written against the real schema names — which caught one error already, since the authorization schema is `authz` and not `authorization`, and a check naming the wrong schema would have passed by finding nothing. |
| 67 | No index review has been done | Medium | Phase 17 lists one and it is not done. The indexes that exist were each added for a named query, so this is about finding the ones nobody thought of — which needs `pg_stat_statements` against realistic data rather than reading the model. It belongs with the load testing in Phase 20. |
| 68 | ~~No `adding-a-module.md`~~ | — | ✅ **Resolved.** `docs/development/adding-a-module.md`. It is not a file listing, because the contract already says what endpoints look like — it is the reasoning, and every rule in it names **the test that fails the build when the rule is broken**: `LayerDependencyTests`, `SchemaBoundaryTests`, `MigrationCoverageTests`, `EndpointSecurityTests`, `RateLimitingTests`, `StepUpCoverageTests`, `AuditCoverageTests`, `SettingCoverageTests`, `InstrumentCoverageTests`, `WorkflowBoundaryTests`. So a module built by half-remembering the pattern stops at the build rather than reaching a merge. It opens with the question worth asking first — whether the thing is a Platform module at all, where the answer is usually no — and each rule carries the defect that produced it, including the two that shipped: `ApplySnakeCaseNames` defined and never called, and the test factory migrating only the kernel for three phases. **Writing it found a claim that was not true.** The document said `[FromServices]` was enforced by a test; it was not, and had not been since Phase 1 — it was enforced by a tripwire, since mapping a route whose service parameter is unregistered in the architecture harness throws `Failure to infer one or more parameters` and turns the suite red with a message that says nothing about the rule. `ServiceBindingTests` now translates that exception into the rule it broke, and catches the parameters that do infer successfully because their type happens to be registered. Verified against a deliberately unmarked parameter. |
| 73 | ~~Readiness answered 503 when the document bucket was unreachable~~ | — | ✅ **Resolved, and it was live for three phases.** The composition root registered the storage check with `failureStatus: Degraded` and the runbook described it that way — but `failureStatus` applies **only when a check throws**, and this check caught its own exception and returned `HealthCheckResult.Unhealthy`. A returned result overrides the registration silently. So readiness answered 503 whenever object storage was unreachable, which empties every instance out of the load balancer over a bucket: the Phase 10 outage in different clothes, where a folder only Documents needed stopped every module from starting. Nothing could have found this by reading — both halves are individually correct and they disagree only at runtime. The check now returns `context.Registration.FailureStatus`, so the decision exists in exactly one place; a second copy of it is what caused this. The guard is the test that found it, which asserts 200 and the word *Degraded* with storage injected broken. |
| 74 | The load test has never been run against realistic data | Medium | `tests/load/platform-load.js` turns ARCHITECTURE.md §24's four budgets into k6 thresholds, so a regression is a failed run rather than an opinion — which is what that section says budgets are for, and it had been prose for twenty phases. It has not been executed: numbers from an empty database measure the framework, not the Platform. It needs a seeded environment of plausible size, which is a Phase 19 environment, which waits on Q4. Every endpoint it names was checked against the generated contract rather than remembered, because a script nobody runs is a script whose errors nobody finds. |
| 75 | No external penetration test | **Medium** | Phase 20 asks for one and it is an engagement to book, not code to write. The Platform has automated dependency and container scanning on every push, a secret scanner over full history, and an exhaustive authorization matrix — none of which is a person trying to get in. It needs scheduling well before go-live. |
| 71 | ~~Nothing exercised the readiness degradation path~~ | — | ✅ **Resolved.** Phase 14 chose `Degraded` for document storage and `Unhealthy` for the database, and the difference had never been executed — a load balancer reads the status code, and a storage check reporting unhealthy would empty every instance out of rotation over a bucket, which is the Phase 10 outage through a different door. Four tests: readiness answers 200 and the word *Degraded*, the rest of the API still answers, liveness is untouched, and readiness is *Healthy* when nothing is broken — that last so the others are showing an injected failure rather than a Platform that is degraded all the time. |
| 72 | ~~The outbox retention sweep shipped untested~~ | — | ✅ **Resolved.** Seven tests, four of them about rows that must survive. It also answered a question unreadable from the code: `ExecuteDelete` with a bound `Take` does translate on Npgsql. Had it not, the sweep would have thrown once every six hours, the job journal would have recorded a failure nobody is watching yet, and the table would have grown for ever while the code looked correct. |
| 76 | ~~A forced password change was enforced by the portal, not by the API~~ | — | ✅ **Resolved.** `IdentityErrors.PasswordChangeRequired` was declared and referenced by nothing. The portal read `mustChangePassword` from `/me` and redirected, which covered everybody who used a browser and covered nobody who called the API directly — so a temporary password issued by an administrator, a password one other person knows, was a working credential for the entire API until its holder happened to open the portal. A forced change exists precisely so that window is short. The obligation now travels as a token claim, so the check costs no query and expires with the token; a middleware after authorization refuses everything except four endpoints marked in metadata rather than by route — changing the password, reading one's own profile, signing out, and anything anonymous. 403 and never 401: the credential is valid, and a 401 would send a client back to re-authenticate, which succeeds and changes nothing. 6 integration tests, including one asserting an account that owes nothing is unaffected — without it the suite would pass on a build that refused everybody. Found while wiring the end-to-end seed. |
| 69 | Test rate limits are raised through a process-global environment variable | Low | `PlatformApiFactory` publishes `CCP_RateLimits__Authentication` into the process environment, so a host built later in the same process inherits whatever the last factory set. `RateLimitTests` deliberately drives that number down to three; a suite that raised it afterwards would make that test fail, and test classes run in parallel with no defined order. The new authorization matrix needs its limits lifted and therefore uses `UseSetting`, which is per-host, rather than joining the problem. The existing mechanism is untouched and still fragile. |
| 70 | The new integration suites have never run on this machine | Low | PostgreSQL 17 is installed and running, and `pg_hba.conf` requires `scram-sha-256` with a superuser password nobody here has (the original cause of #1). Docker will not start either. So these were written against the real schema and verified by CI rather than locally — which is how every integration suite in this project has shipped, and is worth naming rather than implying. |
| 57 | ~~A provider can only be turned on and off from the screen~~ | — | ✅ **Resolved.** Two forms behind `platform.integrations.manage`: registration (code, name, base address) and settings (the five resilience numbers, the redacted field list, the credential reference). **The credential field holds a name and the form says so twice** — in its hint, and in the refusal when the Platform rejects a pasted value, because somebody who has just pasted an API key needs to be told it was not stored rather than that something went wrong. It is not a password field: hiding it would teach the person that a value belongs there. Registration deliberately does not ask for resilience — a provider arrives with the defaults and is tuned afterwards, since a form demanding five numbers before it will accept anything gets answered by guessing and the guesses then look like decisions. Endpoints stay an API call and that is written down as a choice: they are the part of a provider that business applications depend on by key, so adding one belongs with the change that needs it. 30 catalogue keys at parity. |
| 58 | ~~Settings can only be changed at Platform scope from the screen~~ | — | ✅ **Resolved.** The edit dialog takes a scope: the Platform, the company once one is set up, and every registered application — **by name**, since a GUID is no help to the person deciding. Emptying the value box clears the override at the chosen scope, which is what "remove this exception" means, and the box reloads whenever the scope changes so that saving cannot quietly copy one scope's value onto another. The overrides column now **lists the scopes instead of counting them**: a number told somebody an exception existed and refused to say where, which is the one thing worth knowing when a setting behaves differently for one application than for everybody else. An override naming a company or application that has since been removed still resolves, so it is still listed — under its raw identifier, because printing nothing would make a live override invisible. The scope and its identifier are carried together as one value throughout: apart they are a trap, since `Company` with a null id and `Platform` carrying one are both things a form produces by accident and the second resolves to nothing for ever without failing. 8 frontend tests; the scope-identifier match was checked against a deliberately broken comparison. Pure portal work — the API has taken a scope since Phase 13. |
| 59 | ~~Flag targeting cannot be edited from the screen~~ | — | ✅ **Resolved.** A picker listing the roles and units that actually exist, read from the role list and the unit tree — a flag is aimed at *Finance* or *Approvers*, and the GUIDs the Platform stores are no help at all to the person deciding. The tree is flattened to a checkbox per node, indented by depth with a logical property so it reads the right way round in Arabic; anything cleverer would be a picture of a decision the Platform does not actually make, since it stores a flat set of unit ids. The hint that **nothing selected means everybody** is shown above the choice rather than below it, because "nothing selected" reads as "reaches nobody" to almost everyone and means the opposite. The dialog still opens when one of the two lists fails to load: a flag can be aimed at roles while the unit tree is unreachable, and refusing to open at all would be worse than offering half the choice. |
| 14 | Employee custom-attribute extension bag not built | Low | Planned in ARCHITECTURE.md §7.2.2 so business apps attach metadata without a Platform schema change. Needed before the first business system integrates. |

---

## 8. Decisions log

| Date | Decision | Where |
|---|---|---|
| 2026-09-06 | ADR-001 … ADR-015 | `docs/architecture/adr/` |
| 2026-09-06 | Five phase-order deviations, each with a stated reason | PROJECT_PLAN.md §2 |
| 2026-09-06 | .NET 10.0.400 pinned in `global.json` | Phase 1 |
| 2026-09-06 | Architecture tests by reflection rather than NetArchTest | Phase 1, §3.2 |
| 2026-09-06 | Explicit `[FromServices]` on endpoint service parameters | Phase 1, §3.2 |
| 2026-09-06 | Warnings as errors; suppressions require written justification | `.editorconfig` |
| 2026-09-07 | Security headers applied via `OnStarting` so error responses keep them | Phase 2, §3A.4 |
| 2026-09-07 | Each module maps the kernel outbox table into its own DbContext, so events commit with their change | Phase 2, §3A.4 |
| 2026-09-07 | Module-scoped `IIdentityOutbox` / `IIdentityUnitOfWork` rather than shared kernel interfaces | Phase 2 — avoids DI binding the wrong context once a second module exists |
| 2026-09-07 | `[AuthenticatedUserOnly]` as a third explicit access declaration | Phase 2, §3A.4 |
| 2026-09-07 | Usernames normalised to lower case on write | Phase 2 — makes uniqueness enforceable by an ordinary unique index |
| 2026-09-07 | One `PasswordSetter` for every route to a new password | Phase 2 — three callers each re-implementing policy, breach, history and revocation means one eventually forgets |
| 2026-09-07 | Forgot-password always returns 202, whatever the input | Phase 2 — any observable difference makes an anonymous endpoint an address-list discovery tool |
| 2026-09-07 | Breach screening fails open and is off by default | Phase 2 — an outage must not block account recovery; enabling a third-party call is the owner's decision |
| 2026-09-07 | Bootstrap seeder refuses to run once any user exists | Phase 2 — it can only ever create the first account, never a back door into a live system |
| 2026-09-07 | User search uses PostgreSQL `ILIKE` rather than `ToLower().Contains()` | Phase 2 — the `StringComparison` overloads are untranslatable and `ToLower()` applies a function to every row |
| 2026-09-08 | Departments, sections and centers are one `OrganizationUnit` with a type, not three tables | Phase 3 — three tables make every hierarchy query a union and bake today's levels into the schema |
| 2026-09-08 | Materialized path with leading **and trailing** separators, indexed `text_pattern_ops` | Phase 3 — the trailing separator stops one unit's path prefix-matching a sibling; the operator class is what makes the index usable for `LIKE 'prefix%'` |
| 2026-09-08 | Reporting cycles prevented by walking the chain, not by a second materialized path | Phase 3 — manager changes are far more common than unit moves, and chains are short |
| 2026-09-08 | Both language names required, in two columns rather than JSON | Phase 3 — an optional second language becomes a permanently empty column, and the Arabic UI then shows English names |
| 2026-09-08 | TOTP implemented directly rather than taken from a package | Phase 5 — thirty lines verified against the RFC vectors, versus a third-party dependency sitting in the authentication path |
| 2026-09-08 | MFA enforced at the privileged action, not at sign-in | Phase 5, §3D.3 — refusing sign-in locks people out of the system they need in order to enrol, and action-time enforcement also catches a session stolen after a legitimate sign-in |
| 2026-09-08 | Step-up elevation held in the database and bound to the session, not carried as a token claim | Phase 5 — a claim cannot be revoked before the token expires, and prompt revocation is most of its value |
| 2026-09-08 | The step-up attribute lives in the kernel, its policy in Authorization, its handler in Security | Phase 5, §3D.4 — lets an Identity endpoint demand step-up without Identity referencing Security |
| 2026-09-08 | The step-up endpoint set is pinned by test in both directions | Phase 5 — a minimum-only check would let elevation spread until people worked around it |
| 2026-09-08 | Security events are committed on their own DbContext | Phase 5 — an event recording a failure must survive the rollback of the operation that failed |
| 2026-09-08 | An invalid MFA code is a 400, not a 401 | Phase 5, §3D.6 — a BFF treating 401 as token expiry would turn a mistyped digit into a logout |
| 2026-09-08 | The MFA protection key is configured as a path, and startup fails without it outside Development | Phase 5 — a generated key would change per deployment and silently lock out every enrolled user |

---

## 9. Phase 7 report — what the deployment taught

The frontend was written, deployed, and then found to be resting on three
deadlocks in the backend that no unit test could see and that only a person
trying to use the Platform would hit. All three have the same shape: **a module
that assumes something exists, and nothing that creates it.**

### 9.1 The three deadlocks

**No permissions existed at all.** The seeder derives the permission list from
endpoint metadata — which is right — but read that metadata from the
`EndpointDataSource` registered in the container. That is a composite which never
sees the endpoints a minimal API maps: it resolves without throwing and reports
none. So startup declared zero permissions, granted the administrator role zero
permissions, logged nothing (the seeder only logs on change, and nothing
changed), and every administrative screen answered 403 with no explanation
anywhere in the system. The first administrator could sign in and do nothing.

A second defect sat behind it: `GrantAllPermissionsToAdministrator` reads the
permissions back with a query, and they had not been saved yet. It self-heals on
the next restart, which is exactly why it was invisible — a fresh deployment is
unadministrable until something restarts it.

**Only one role could ever exist.** The seeder creates `platform-administrator`
holding everything; no endpoint created another. So granting anybody anything
made them a full administrator. Scope does not rescue that — it narrows who the
permissions reach, not which permissions they are.

**No company could ever exist.** Every read in the Organization module resolves
the company first and answered `CompanyNotFound` without one. No endpoint created
a company, and `CreateUnit` required one. The whole section reported failure on a
working installation.

In all three the domain layer was complete and correct — `Role.Create`,
`Company.Create`, `AddPermission`, the repository methods, the validation. Only
the application and API layers were missing. **A domain model can be finished and
the product still be unusable**, and no test that stops at the domain will say so.

### 9.1a One claim, read three ways, and the portal looked empty

Found after the three deadlocks and worse than any of them, because it hid
them: **the administration portal showed no administrative control at all, and
no role could be granted or revoked.**

Three copies of "read the subject from the principal" had grown. Two read
`sub` and fell back to `ClaimTypes.NameIdentifier`; the third read only `sub`.
ASP.NET Core's JWT handler renames inbound claims by default — `sub` arrives as
`http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier` — so the
third copy found nothing on a perfectly valid token and its endpoints answered
401 to every caller, always.

Those endpoints were `GET /me/permissions`, which is how the portal decides
which controls to show, and the two that create and destroy access. The
Platform could not tell an administrator what they were allowed to do, and
could not hand out access at all.

Both versions look correct. `FindFirst("sub")` is what anyone would write, and
it is wrong here for a reason that lives in framework configuration three files
away. `CallerIdentity` in the kernel is now the only implementation, and the
host stops the renaming with `MapInboundClaims = false` — a claim should be
called what the token calls it.

It surfaced because a test written to answer "why is this button missing"
checked the session's own view of its permissions *separately* from the
control, and reported which half broke. Splitting the two was the whole point.

### 9.2 The BFF broke every action that succeeded

The Platform answers 204 to a command with nothing to return — changing a
password, moving a unit, granting a role, disabling an account. `relay` decided
success by "did a body come back", so a 204 fell to the failure branch, which
built an error body at status 204 — which `NextResponse` refuses to construct. The
handler threw, the browser saw 500, and the screen reported failure for work the
Platform had already done. The deployed database shows the first password change
succeeding on the day it was reported as broken.

### 9.3 The end-to-end suite had never passed

Not once since it was written. Every report said `waitForURL` timed out, which is
what a screen that never navigates looks like from outside; the reason sat in a
response body nothing printed, and both servers in CI ran with their output
discarded. Making the setup quote the failed calls and the text on screen, and
keeping the server logs, turned a week of "flaky" into one line: `responded 204`
followed by a stack trace.

**The suite now passes in both locales, and the whole pipeline is green for the
first time.**

### 9.3a Two acceptance criteria measured for the first time

axe against WCAG 2.1 AA on every screen in both locales, and four viewport
widths. The responsive rule held everywhere — no page scrolls sideways at any
width, in either direction. Accessibility found one real defect:
`--color-text-muted` at #7b8794 reads 3.66:1 on white, below the 4.5:1 AA
requires, and it is the colour of the word "(Required)" beside every field
label. That word exists because the design refuses a red asterisk — it says
the rule in words so a screen reader announces it — and setting it too faint
to read undid the decision it was serving. Now #616e7c, with a unit test that
computes the ratio for every text token.

Automated checks find perhaps a third of real barriers. Passing them is a
floor, not a certificate.

### 9.4 What was built

Twelve screens, and every one of them can write: company setup, organizational
structure (tree as a table, create, rename, move, deactivate), employees with a
create form, users with create/edit/enable/disable/unlock, role definition and
permission editing, role granting and revoking at a scope, own-account two-factor
enrolment with recovery codes, and a dashboard of live figures. Step-up
refusals are told apart from permission refusals and answered with a prompt that
replays the original action.

### 9.5 Guards added, each watched to fail before being trusted

- Permission seeding, the administrator's full grant, and a loud refusal when no
  endpoint declares a permission — integration tests against real PostgreSQL.
- `relay` on every status shape, including the 204 that caused all this.
- No page or layout may refresh the session: refreshing during render spends the
  refresh token and then cannot store the replacement.
- The role-permission editor added to the reviewed step-up set, which the
  architecture test refused until it was recorded deliberately.

---

## 10. Phase 8 report — a general engine, and the boundary that makes it one

The whole module is one sentence made structural: **it understands states,
transitions, assignees, actions and timers, and it does not understand what is
being approved.**

There is no threshold in it, no amount, no currency, no eligibility rule — and
an architecture test fails the build if that vocabulary appears in any of its
source files. The test exists because the erosion is gradual and reasonable at
every step: nobody decides to put a purchase limit in a general engine; somebody
adds one at five o'clock because the alternative is a conversation about
callbacks, and a year later the module is the purchasing system's approval logic
wearing a general name — unusable by the next system, which is the one thing it
existed to be.

### 10.1 What was built

Definitions are versioned data, registered by applications through the API. A
new approval process for a future system needs no Platform code and no Platform
screen. Publication validates once — every transition target resolves, every
step is reachable — so a process that dead-ends is refused when it is written
rather than found by whoever is waiting on step three.

Versions are frozen. An instance records its version and runs on it to
completion; a definition published tomorrow does not touch a request filed
today.

Six assignee strategies, every one of them answering "which person, by their
place in the company". The seventh case — routing that depends on business data
— is a list the calling application supplies, which is to say it happens
outside the engine.

Escalation raises an event and marks the task. It does not reassign: moving
somebody's work to their manager automatically is a company policy, not an
engine behaviour.

The inbox is the one screen most people in the company will ever use, and it
offers the actions the process actually permits at that step rather than every
verb the engine knows.

### 10.2 A test found a defect before the code ran

Asserting that every action in the enum is named by the state machine failed on
`Return`, which was handled generically. A definition allowing a return without
saying where to would have marked the instance **Approved** — recording an
approval nobody made, on a request somebody had just sent back. Every action is
now named explicitly, an unnamed one is refused rather than defaulting, and the
half-recorded action is rolled back so the history stays honest.

### 10.3 The seam

`IAssigneeResolver` is declared in the Application layer and implemented once in
Infrastructure, against `IOrganizationDirectory` and `IRoleDirectory` — both
Contracts-only. An architecture test refuses any reference from this module to
another module's Domain, Application or Infrastructure. Lift the engine into
another product and that one file is what needs rewriting.

`IRoleDirectory` is new and has one method on purpose. "Who holds this role" is
all Workflow needs; a module that could ask arbitrary authorization questions
would end up making authorization decisions, and there would then be two places
where access is decided.

---

## 11. Phase 9 report — the oldest debt, and what escaping is for

### 11.1 Password reset completes

The token has been issued and staged on the outbox **since Phase 2**. The flow
existed, had tests, and could not finish, because nothing could send an email.
Seven phases later it does.

Email only. An in-app copy would put a working account-takeover credential in
the inbox of the account it takes over — useless to somebody locked out, useful
to whoever locked them out.

### 11.2 Escaping is the whole security story here

A notification body reaches an email client and an in-app inbox, both of which
render markup. A display name containing a script tag is the entire payload of
a stored cross-site-scripting attack if it arrives unescaped, and a display name
is exactly where an attacker puts one.

So every substituted value is escaped, with no way to opt out, and the body
never is — an administrator writing it may legitimately use markup. A unit test
pins the order: `&` before `<`, because the other way turns `&lt;` into
`&amp;lt;` and shows the reader the escape sequence instead of the character.

And there is no template language. Substitution only: no conditionals, no loops,
no property paths. A language stored in a database is code nobody reviews
running with the Platform's privileges.

### 11.3 Two design errors found while wiring the listeners

The workflow completion event carried who **decided** and not who **asked**. A
listener built on it would have told the approver what they had just approved
and told the person who had been waiting for weeks nothing.

And integration events lived in `Domain` in both Workflow and Identity, so
subscribing to one meant referencing a module's internals — the coupling the
Contracts projects exist to prevent. Both sets moved to `Contracts`, which is
where a published shape belongs.

### 11.4 One thing caught only by looking

The module was never added to the host's module list: an earlier edit reported
success and did not apply. The symptom was a contract with no notification
endpoints in it, and the only reason it surfaced was regenerating the contract
and reading the output. A build, a test run and a lint pass would all have gone
green with the module unreachable.

---

## 12. Phase 10 report — the file is not the row

### 12.1 What the bytes say, not what the caller says

The declared content type and the extension are both strings the caller chose,
and both are trivially set to `application/pdf` on a Windows executable. So
neither decides anything: the first bytes do.

`FileTypeInspector` lives in the domain rather than the infrastructure, because
"which files may this company store" is a rule and not a detail of how HTTP
works. It reads a header, never a whole file, and it names what it refused — "a
Windows program", "a script" — because the common cause is a rename to get past
an extension check and the second most common is an honest mistake, and both are
cleared up by being told what the file really is.

Text is decided last and by exclusion, after every signature has had its turn. A
PNG whose header happened to decode as text would otherwise be stored as a text
file.

### 12.2 The order of the upload pipeline is the design

Buffer, measure, identify, hash, scan, write. Each step can refuse, and a
refusal at any of them leaves nothing behind.

Two decisions in there are worth stating. The **size limit stops the copy**
rather than checking a length afterwards — checking afterwards means having
already written a two-gigabyte file to disk to discover it was not allowed,
which is the denial of service the limit exists to prevent. And the content
reaches storage **before** the version row exists, so a row that exists is one
whose content exists; the other order costs somebody their file, this one costs
a little unreferenced storage.

### 12.3 The scanner does not lie

The Platform ships without one, because bundling a scanner means choosing a
vendor, a licence and a deployment shape on behalf of every company that installs
this.

What it does not do is claim otherwise. The default reports `NotScanned` rather
than `Clean`, that verdict is written into the access-log entry for the upload,
and so a version record says plainly whether anything ever looked at the file. A
default that reported "clean" would produce an audit trail asserting that every
file the company holds was checked — a false record, which is worse than none.

A scanner that throws is recorded as *failed*, not as *clean*. Swallowing that
into a pass is how an outage becomes an infection.

### 12.4 One evaluator, and it logs the refusals

Access checks do not go wrong because somebody writes a bad one. They go wrong
because somebody writes a *second* one, slightly different, and the two disagree
— a document appears in a search that its finder cannot then open, or worse.

So `DocumentAccessEvaluator` is one pure function, and every path reaches it:
upload, download, sharing, deletion. The search filter is the only restatement,
because one of them has to run in SQL, and the pair is what the integration tests
exercise from both ends.

The guard that calls it also writes the log entry, in the same class, for the
same reason: a handler that checked access itself would record the successes —
that is the part somebody remembers — and **the refusals are the half of a
document access log worth reading**. One person failing to open a document is a
wrong link; one person failing to open forty is something else.

A denial is committed on its own before the failure is returned. It has to be:
the request is about to end without saving anything, and a denial recorded in a
transaction nobody commits is a denial nobody can see.

### 12.5 Deleting twice, and keeping the record of what was destroyed

Mark, then purge after thirty days. Two stages because otherwise a misclick and a
legal instruction look identical to the system, and only one of them should be
able to destroy something.

The purge destroys the content and keeps everything said about it — the file
name, the size, the hash, the whole access history. "This document existed, these
people read it, and it was destroyed on this date by this person" is the question
asked *after* a deletion, and a removed row cannot answer it.

The sweep deletes the bytes before it marks the record, which is the order that
can only fail in the harmless direction. Marking first and then failing to delete
would leave a document recorded as destroyed whose content is still in the
bucket: a false statement in the one place a company will be asked to prove
something.

### 12.6 The link that cannot be a foreign key

A business system files a document against `purchase-order` / `PO-2026-0041`, and
the Platform stores two strings. There is no constraint and there cannot be one:
the Platform is built before the systems that use it and must outlive any of
them, so a foreign key into `purchasing.orders` would make this module
undeployable without the purchasing system and undeletable with it.

The honest consequence, stated rather than hidden: a link can outlive the record
it points at. That shows as a link the caller cannot resolve, which is much
better than a module that refuses to start.

### 12.7 What was added to another module, and why

`IRoleDirectory` gained one method — the roles a person holds. It is the same
question as the existing one from the other end, and it earns its place because
the alternative is fetching the full membership of every role named on a document
on every request to answer a question about one person. It returns role
identifiers, not permissions: a caller can compare them against rules it owns and
cannot make an authorization decision with them.

---

## 13. Phase 11 report — the same rules, for something that never sleeps

### 13.1 Machines hold roles, not a second thing that looks like roles

The obvious design is a separate vocabulary — API scopes, granted at
registration, checked by their own code. It is obvious because every OAuth
tutorial has one, and it is wrong here: a second permission language has to be
kept in step with the first, and the day they disagree nobody can say which is
authoritative.

So an application holds the same roles, at the same scopes, resolved by the same
evaluator through the same version-stamped cache. Two grant tables and one
algorithm. `RequirePermission` needed no change to work for machines, which is
the test of whether the reuse was real.

The one thing that did need a new type was the cache key. A user id and an
application id are both GUIDs from different tables, and a key that ignored the
difference would be one copy-paste away from answering a question about a machine
with a person's answer.

### 13.2 Delegation is an intersection, and both halves matter

An application acting for a person may do only what the application is trusted
with **and** what that person is entitled to, at the narrower of the two scopes.

Either half alone is a real vulnerability, and neither is visible from outside —
both look like a working integration. Taking the person's rights alone makes
every registered application a way to act as anybody it can name. Taking the
application's alone lets it read what the person it claims to be acting for
cannot. Seven unit tests pin the pair, including the case where both hold the
permission over departments that do not overlap and the answer is therefore no.

### 13.3 The claim that stops an application being mistaken for a person

A machine token acting as itself carries an application id in its subject claim.
That parses as a `Guid` perfectly well, and every handler in the Platform that
says "the caller's own records" would have used it as a user id.

So the subject now carries its kind, and `CallerIdentity.TryGetUserId` refuses an
application. An endpoint written for people answers 401 to a machine rather than
quietly operating on somebody's data. An integration test holds that line.

### 13.4 Rotation is designed to be boring

Two live credentials at once, and `LastUsedAt` stamped on every exchange.

That is the whole feature, and it is the difference between a rotation that
happens and one that is discussed. A model with one live secret makes every
rotation an outage, so nobody performs one; a model with no usage timestamp makes
the last step a guess, so nobody finishes one. Both failures end at the same
place — a secret from 2026 still in production in 2029.

### 13.5 SHA-256, deliberately

The hash on a client secret looks like the mistake everybody is taught to avoid,
so the reasoning is written where somebody will find it before "fixing" it.

Argon2 exists to make *guessing* expensive, and guessing is only worth attempting
against a secret a human chose. This one is 32 bytes from a cryptographic
generator: no dictionary, no reuse from another site, nothing to guess. What a
slow hash would cost is real — the token endpoint sits on the path of every
machine call in the company, and a 50-millisecond hash there is a
denial-of-service amplifier an attacker can trigger with no valid credential at
all. The property that matters, that a database disclosure hands nobody a working
credential, is preserved.

### 13.6 What the documentation caught

Writing the integration guide found two things wrong: it described a permission
manifest endpoint that did not exist, and it called the authorization check with
the wrong method. Both were found by reading the generated contract rather than
by writing more carefully.

That is worth recording as evidence, because Phase 11's acceptance criterion says
the guide must be validated by having an outside developer actually try — not by
self-assessment. Checking against the contract is a stronger form of
self-assessment and it still is not the criterion. It is recorded as open debt.

### 13.7 What was deferred, and why it is not an omission

Webhook subscriptions are listed here and again in Phase 12, which owns outbound
calls, their resilience policy and the host allow-list that keeps a subscription
from being a way to make the Platform fetch arbitrary URLs. Building them twice
would be worse than building them once, in the phase that has the machinery.

---

## 14. Phase 12 report — the door, and what happens when it is not there

### 14.1 The attack this exists to prevent

A layer that will fetch a URL somebody else chose is a proxy into the network it
runs in. That sentence is the whole justification for the allow-list, and it is
worth being concrete about, because "server-side request forgery" sounds abstract
until the list of what is reachable from inside is written down: the cloud
metadata service at `169.254.169.254`, which hands out the instance's credentials
to anything that asks; internal admin interfaces, unauthenticated because they
are "not reachable from outside"; and the Platform itself.

So the policy is deny by default, and an empty allow-list is a Platform that
makes no outbound calls at all. That fails visibly on the first call and is fixed
in a minute. The other default fails invisibly, and the failure is unbounded.

Two independent checks, because one is not enough. The **name** must be on the
list — and every **address it resolves to** must be public, since a name on the
list today can be pointed at `127.0.0.1` tomorrow by whoever controls its DNS.

### 14.2 The gap that is not closed, stated rather than hidden

Between the check and the connection, the name is resolved again. DNS rebinding
lives in that gap, and closing it means connecting to a checked address rather
than to a name — taking over socket connection in the HTTP handler.

It is recorded as debt with the reason, not quietly omitted. The allow-list
narrows it a long way: an attacker needs control of a host somebody deliberately
allowed, which is a much narrower position than the general case.

### 14.3 Credentials by reference, and the column that does not exist

The providers table holds `integrations/acme-bank/api-key`. It does not hold a
secret, and there is **no column that could**. A database backup that leaks is
therefore not a credential leak, and rotation is an operation on the secret store
with no deployment.

The domain refuses a value pasted where a name belongs — anything long, or
carrying a recognisable prefix. That is a guard rail and says so; the guarantee
is the absent column.

The default resolver reads the environment, and confines references to a
`Secrets` section. Without that confinement a reference of
`ConnectionStrings:Platform` would resolve, and a provider row would be a way to
read the database password out of the Platform's own configuration.

### 14.4 The order of the pipeline is the design

Bulkhead, breaker, retry, timeout — outermost to innermost, and each position is
load-bearing.

The bulkhead is outermost so a slow provider cannot occupy more than its share of
the Platform's threads; inside the retry it would count attempts separately and
mean something different for a provider that retries. The timeout is innermost so
it bounds one attempt; outside the retry it would bound the whole sequence and
the third attempt would inherit whatever the first two left of the budget.

Retries carry jitter, for the reason the notification dispatcher carries it: a
provider coming back from an outage met by the whole backlog at once is how a
recovery becomes a second outage.

And not every failure is retried. A 400 will be wrong again; a 401 cannot be
fixed by repetition. A 429 is retried, because the provider explicitly said
"later".

### 14.5 Redaction happens before storage

The call log is the most valuable thing this module produces and the most
dangerous. It makes a dispute resolvable, and it is a permanent record of every
payload the company exchanged.

So the provider's declared fields are blanked **before the row is written**.
Storing the real payload and hiding it at read time leaves the secret in the
database, where the next query, the next export and the next backup will find it.

Which fields are sensitive is per provider, because only its owner knows. And
matching is by field name at any depth rather than by path: path matching is more
precise and misses the same field one level deeper than whoever wrote the policy
expected, and the failure mode of being too precise here is a leak.

### 14.6 The timestamp goes inside the signature

An inbound webhook is untrusted input from the internet — the URL is guessable
and often published, and anybody can post to it.

The subtle part is that the timestamp is part of the signed material rather than
a header beside it. As a header alone, an attacker replaying a captured request
would simply change it, and the five-minute window would be checked against a
value they control. Signed, it cannot move.

And a valid signature is not enough on its own. A correctly signed request that
arrives twice is authentic both times: the signature proves who sent it and says
nothing about whether it has already been acted on. Every accepted signature is
remembered until the window passes, with a unique index to settle the race when
two copies arrive at the same instant.

### 14.7 A defect that stopped everything, found in production

Not part of this phase's plan, and the most important thing in it.

The container came up and died. The Documents module's local storage provider
created its root directory in its constructor; the image runs as an unprivileged
user, the application directory belongs to root, the call was refused — and
because that provider is a singleton resolved while the host is being built,
identity, authorization, workflow and everything else failed to start. Because
documents could not create a folder.

The lesson is not about directories. It is that **a constructor resolved at
startup is a place where any failure is total**, and one module's optional
convenience had been given that power. The fix moves the I/O to first use, where
a failure fails an upload. Three tests now pin it, and the first two would have
caught it before it shipped.

---

## 15. Phase 13 report — the table that must not hold secrets

### 15.1 The refusal that matters most

A settings table is stored in plaintext, exported, backed up, and shown on a
screen. A secret put there is a secret in all of those places — and the person
who put it there did so because it was convenient, which is exactly when it
happens.

So a value that looks like a credential is refused outright: a PEM block, a
recognisable token prefix, a JSON Web Token, a connection string carrying a
password, a long high-entropy string. A test asserts that the refusal holds
**even when the setting is marked sensitive**, because that is the reasoning a
person would use to get around it — and sensitivity stops a value being read
back, not being there.

The check is a heuristic and says so in its own documentation. It catches the
recognisable paste and will not catch a short password somebody typed; one that
tried to would refuse half the legitimate values in the Platform. The rest is a
documented rule and a review.

### 15.2 Sensitivity has to reach the change history

A value that cannot be read back through the API but sits in plain sight in its
own change log has not been protected. It has been moved.

So the history of a sensitive setting records that it changed, by whom, when and
why — and not what to. Everything the history exists for survives; none of it
needs the value.

### 15.3 Narrowest wins, and nothing else would work

Application beats company beats Platform. It is the only precedence rule anybody
can hold in their head under pressure.

The alternative — where something broader can override something narrower —
produces the case where changing a Platform default silently undoes a
deliberate local decision, and nobody finds out until the behaviour is wrong
somewhere specific.

A row exists only where somebody overrode something, so adding a setting costs
nothing and a company that customised three things has three rows rather than
four hundred.

### 15.4 The same caching mechanism as permissions, deliberately

A version stamp in the database, not a time-to-live.

A time-to-live leaves a window — however short — in which a capability somebody
deliberately switched off is still on, and "however short" is not a property
anyone can reason about at the moment they are deciding whether to switch it
off. The stamp is in the database rather than in memory so an instance that did
not make the change still notices it; without that, a Platform on three
instances applies a change to one of them.

It is the mechanism the permission resolver already uses. Two caching strategies
in one Platform is one more thing to reason about during an incident.

### 15.5 Flags are targeted by role and unit and by nothing else

Not by percentage, not by arbitrary attribute, not by a rule language. Each of
those turns "who has this?" into a question needing a simulator, and a flag
nobody can reason about is worse than no flag.

Three defaults are load-bearing and all three are tested. A **new** flag is off,
so one created ahead of the thing it guards does not release it when the row
appears. An **undeclared** flag is off, so a typo is not a silent launch. And
**off is off** however a flag is targeted, which is what makes the master switch
trustworthy at eight in the evening.

Targeting nothing means everybody rather than nobody — a flag that was on and
reached nobody would look broken and be working, which is the worst combination
available.

### 15.6 What the phase does not yet have

The module works and **nothing uses it**. Every retention period, size limit and
interval written across Phases 9 to 12 is still an `appsettings` value needing a
deployment to change, which is exactly what this module exists to fix. The
benefit is available and unclaimed, and that is recorded rather than implied.

---

## 16. Phase 14 report — one identifier, and the redaction that cannot be forgotten

### 16.1 The correlation id goes on the span where it is decided

Not in the tracing configuration, which was the first attempt. The value lives in
a scoped service rather than on the request, so the enrichment callback could not
see it — and rather than copy it somewhere the callback could reach, it is
written onto the current span by the middleware that decides it.

That is one place. A second reader of the same value is a second place to get it
wrong, and the whole point of §22.1 is that the identifier is the *same* one
everywhere.

### 16.2 Redaction belongs at the sink

The architecture says "applied at the sink, not left to the discipline of whoever
writes the log statement", and that phrasing is the design rather than a
preference.

Every leak of this kind is written by somebody being careful. They did not know
the object they logged carried a token three properties down, or that the
exception message contained the request body. Discipline does not scale to every
log statement anybody will ever write, and a review that catches it today will
not be there in two years.

So two rules run over every event: a property whose *name* means a credential is
blanked whole, and a *value* that looks like one is blanked wherever it appears.
Twenty tests pin both, including that ordinary text survives — a redactor that
blanked everything would be a log nobody can use and would be switched off within
a week.

It is a last line and says so. A redactor people rely on instead of not logging
secrets will eventually meet a shape it does not recognise.

### 16.3 What is not instrumented, and why that is the point

The framework already emits request rate, duration and error rate for every
endpoint; the database and the HTTP clients emit theirs. Adding the Platform's
own versions would produce two numbers for one thing and an argument about which
is right during the incident where it matters.

What is added is the five things the framework cannot know, and each one answers
a question somebody actually asks: is this a failure spike or a busy Monday, is
somebody mapping what they can reach, did the sweep run, are events getting out.

Two shapes were chosen carefully. Authentication is counted as **attempts with an
outcome tag** rather than as failures, because a failure count alone cannot
distinguish a spike from everybody arriving at nine. And denials are tagged by
**permission rather than by caller**, because a caller id would put a person's
identifier into a metrics backend, which is not a place personal data belongs.

### 16.4 Degraded is not unhealthy

Readiness now checks document storage as well as the database, and a failing
store reports **degraded** rather than unhealthy.

Taking the whole Platform out of rotation because a bucket is unreachable would
be the same mistake, in a different costume, as the defect that stopped the
Platform starting over a folder it could not create. Documents stop; identity,
authorization and workflow do not.

### 16.5 What this phase does not have

A screen, and a stored history of background job runs. Both are listed in Phase
14 and both belong with the Platform dashboard, so they are recorded as debt
rather than half-built here.

And the alerts exist as a runbook rather than as deployed rules, because creating
them is an operation in a backend nobody has chosen yet (Q4). Seven conditions
with thresholds and a first action each — written down, and not yet watching.

---

## 17. Phase 15 report — the part that is not a screenshot

Two modules had a complete API and no screen. Both now have one, and the whole
portal is in the accessibility, responsive and signed-in sweeps in both locales.
That is the summary. What follows is the part that is not obvious from a
screenshot.

**The call log shows both payloads in full, and that is safe for exactly one
reason.** The redaction happened before storage. What an administrator reads is
what the database holds; there is no unredacted copy anywhere for the next
export, backup or support ticket to find. Had redaction been a display concern,
this screen could not exist — showing a payload would mean deciding, per field,
per screen, forever, whether it was safe, and being wrong once. The decision
that made this page possible was made three phases ago in a different module.

**Neither screen shows a credential, and no line of code is responsible for
that.** The Platform stores the *name* of a secret. There is no column, no DTO
field and no response shape that could carry a value, so the integrations screen
prints the reference because that is the only thing there is to print. The same
holds for a sensitive setting: its value is absent from the Platform's own
response, so the configuration screen is not hiding it — nothing arrived. A
screen that hides a value it received is one careless render away from showing
it. A screen that never receives one is not.

**Idle is grey.** A provider nobody has called yet is not healthy; nothing is
known about it. Painting it green would be the screen inventing an assurance
from an absence of evidence, which is the failure mode of every status board
that ever lied during an outage. Failures are shown as `3 / 4` rather than 75%,
for the same reason: the fraction says how much the number is worth and the
percentage does not.

**One defect found by writing the screen.** The health line stamped itself with
`new Date()` inside the render, so it printed the current clock every time
anything on the page changed — a page reporting figures computed on mount and
labelling them with the time an operator happened to click something else. It
now records when health was read, so the stamp goes stale in front of whoever is
watching it. Nothing refreshes on this page; that is the honest thing to show.

**The refusal is explained, not reported.** Pasting a credential into a setting
comes back as `CONFIG.SECRET_SHAPED_VALUE`, and the screen answers with why: a
setting is stored in plaintext, appears in backups and exports, and a secret
belongs in the secret store and is named here by reference. Rendered as a
generic validation error, the reasonable next move is to try a value that gets
past the check — which is the outcome the check exists to prevent.

**What was left out, and recorded rather than glossed.** Registering a provider
and editing its resilience, redaction and credential reference is still an API
call (#57). Settings can only be changed at Platform scope from the portal
(#58), which is the scope where narrowest-wins matters least. Flag targeting
cannot be edited (#59) — and would not yet be useful if it could, because #50
means the feature-state endpoint answers *off* for every targeted flag. Those
two are one piece of work and are named as one.

---

## 18. Phase 16 report — the alert that was green because nothing emitted it

The phase was meant to build a page. It built a page, and on the way it found
that the thing the page was supposed to display did not exist, and that the
alert watching it had been reporting health it had no way of knowing.

**`ccp.jobs.runs` was declared in Phase 14 and called by nothing.** The meter
class sat in `CCP.Kernel.Api`. Every background sweep sits in a module's
Infrastructure project, and no module's Infrastructure references the API layer.
So `BackgroundJobRan` was unreachable from the only five places with any reason
to reach it. Nothing failed to compile — nothing tried. The runbook's "background
job failures: any 3 consecutive" watched a counter that was structurally
incapable of moving, which means it read exactly the same whether the sweeps were
running perfectly or had stopped in the night.

Three things made it survive a phase, a review and a green build:

- It is a **missing call**, not a wrong one. Every test that existed passed,
  because there is nothing to fail.
- The metric had a name, a description, a unit and an alert. Everything about it
  looked finished except the part nobody can see from reading it.
- Reading the sweeps did not reveal it either. Each had a sensible
  `try`/`catch` and a log line; what was absent was absent from all five equally,
  which is what absence of a shared behaviour looks like when nobody has written
  the shared thing yet.

That is now #63, still open: **nothing anywhere asserts that a declared
instrument is emitted.** An architecture test could assert that every public
method on `PlatformMetrics` has a caller outside its own assembly, and it would
have failed on the day the method was written. Recording it as debt rather than
closing the phase quietly, because the specific bug is fixed and the class of bug
is not.

**The fix moved the meter and centralised the behaviour.** Instruments belong
where the work they measure can see them, so `PlatformMetrics` is in the
application layer. The five periodic sweeps now run their pass through one
`JobRunner`, which times it, records the outcome, and swallows what it throws —
because a `BackgroundService` whose `ExecuteAsync` throws stops for the life of
the process, and one bad pass would silently retire a sweep until somebody
redeployed. Each sweep's own `try`/`catch` was doing part of this, differently.

**A shutdown is not a failure.** A pass cancelled by the host stopping is
recorded as stopped. Counting it as failed would make every deployment produce
failures, and an alert that fires on every release is one people learn to close.

**The history says what the pass did, not that it happened.** "Removed 412 call
log entries" and "removed 0" are different facts. A history that recorded only
that a job ran would show a green row every hour for a sweep whose query had
quietly stopped matching anything — which is the exact failure a job history is
supposed to catch, reproduced inside the tool built to catch it.

**A job that stopped running still appears.** The summary is the newest run of
every job *plus* the last day's runs. Assembled from the window alone, a job that
died last week would have nothing in it and its row would vanish rather than turn
red, and a row that is silently absent is worse than no page at all. That has an
integration test on it by name.

**The record of a failed pass must not roll back with the pass.** The journal
writes in its own scope and its own transaction — the same reasoning that keeps a
refused document access committed on its own. And the history prunes itself on
write rather than having a sweep of its own, because a job history kept bounded
by a background job would have exactly one job whose failure nothing records, and
it would be the one that fills the disk.

**What the outbox panel leads with is the age, not the depth.** Four hundred
pending messages is either a busy minute or a relay that stopped on Sunday. The
depth cannot tell those apart and the age of the oldest one can.

**Left out and recorded.** The outbox relay and the notification dispatcher are
not journalled (#61): both poll continuously, so a row per pass would be twenty
thousand a day drowning the four that answer a question — their health shows up
as queue age instead. The summary line is not grouped by instance (#62), so on a
two-instance deployment a job failing on one machine reads as intermittent rather
than as one broken machine; the run history does show it. And the seven alerts
are still documentation (#55), because the backend they would be created in is
still undecided.

**A postscript, written after the phase was otherwise finished.** The guard for
#63 — an architecture test asserting that every recording method on
`PlatformMetrics` has a caller — was added last, as a way of closing the class of
bug rather than only the instance. It failed on its first run, on a second
instrument: `ccp.outbox.dispatches`, declared in Phase 14, never wired to the
relay it describes, answering "are events getting out?" with silence for three
phases. So **two of the five instruments the Platform declares were emitted by
nothing**, both had prose written about them, and both read as healthy. The one
found by hand took three phases and a phase dedicated to the subject. The one
found by the guard took under a minute. That difference is the whole argument for
writing the guard rather than only fixing the bug.

---

## 19. Phase 17 report — the limits, and the backup that does not exist

Three things came out of this phase: the database can no longer be taken down by
one query, the oldest debt in the register is closed, and a restore can be
checked rather than believed. One thing did not: **nothing is taking a backup**,
and that is now the highest-severity item in the project.

**The timeouts are enforced by PostgreSQL, and that distinction is the whole
point.** A client-side command timeout stops the *application* waiting. It does
nothing at all to the statement, which carries on burning the server's CPU and
holding its locks on a connection nobody is listening to any more. Only the
server can actually stop it, so `statement_timeout` and
`idle_in_transaction_session_timeout` are sent as connection options.

The second of those is the one that causes outages. An abandoned open
transaction holds its locks *and* stops `VACUUM` reclaiming any row version
newer than itself, so tables bloat, the planner's estimates rot, and at the
extreme transaction id wraparound protection begins refusing writes across the
whole database. Nothing on the client side can clean it up — by definition, the
client is the thing that went away.

**They live in the connection string, not in each `DbContext`.** There are
twenty-two `UseNpgsql` call sites in the Platform. A rule that has to be
repeated twenty-two times is a rule that is missing from at least one of them,
and this project has already produced that exact defect twice this week in a
different form. Putting it in the string the composition root resolves means
every context, every design-time factory and every raw connection inherits it
without knowing it exists.

**The migrator is exempt, and the interesting half is not the obvious half.**
Obviously an index build on a large table is legitimately minutes of work, and a
schema change killed halfway through by a limit meant for web requests is worse
than what the limit prevents. Less obviously: the migrator takes a PostgreSQL
advisory lock, `pg_advisory_lock` **blocks** until it is granted, and a statement
timeout applies to a blocking statement. So a second instance queuing behind a
long migration would have its wait cancelled after sixty seconds and would then
go on to serve requests against a half-migrated schema. That is a worse outcome
than anything the timeout was protecting against, and it was found by reasoning
about the lock rather than by a test — there is no test that can produce it
without two instances and a slow migration.

**A guard that looked right and did nothing.** The first version skipped any
setting the deployment had already made, using `builder.ContainsKey`. A
strongly-typed `DbConnectionStringBuilder` answers true from `ContainsKey` for
every keyword it knows about, set or not — so the guard short-circuited every
time and applied none of the limits, while reading exactly like a guard that
works. Nothing failed; the connection string simply came back unchanged. It was
caught by tests asserting the resulting values rather than by review, which is
the argument for writing them that way round. `ShouldSerialize` is the API that
reports what the caller actually set.

**The oldest debt in the register is closed.** #4 — outbox cleanup — had been
open since Phase 1. Every state change in every module writes a row there, so
the table grows with the company's activity and never with its size. Delivered
rows are now kept a week and removed in bounded batches, so the first pass on a
table nobody has ever pruned drains over hours instead of holding one enormous
transaction on the busiest table in the database.

**Dead-lettered rows are never swept, and that is not an oversight.** A dead
letter is an event that will never be delivered — an audit entry or a
notification permanently missing. A timer that quietly erased those would erase
the evidence of the one failure the whole outbox mechanism exists to make
visible. They stay until a person deals with them, and the Operations screen
counts them.

**The restore is the thing worth practising.** `scripts/verify-restore.sh`
answers what a dump file cannot: is this usable? It restores into a scratch
database and asserts that the audit trail is still range-partitioned — a
property of the table, not of the rows, so a restore that flattened it would
work perfectly and quietly break retention for years — that its partitions came
back, that every module schema is present, and that at least one user, role and
assignment survived. That last group is the one that separates *the file
restored* from *the company can come back*: an empty `identity` schema restores
cleanly and locks everybody out for ever.

Writing those checks caught an error in themselves. The authorization schema is
`authz`, not `authorization`, and a check naming the wrong schema would have
passed by successfully finding nothing.

**What is not done, and one of it is serious.** No backup is being taken (#65).
Continuous archiving and point-in-time recovery are features of a managed
PostgreSQL and the provider is undecided, so this is blocked rather than
deferred — but it is recorded as **High**, above everything else in the register,
because every other risk there is survivable and this one is not. The
verification script has never been run against a real dump (#66), since the local
machine still has no PostgreSQL access. The application still connects with one
role for both DDL and DML (#64). And no index review has been done (#67); that
needs `pg_stat_statements` against realistic data rather than reading the model,
so it belongs with the load testing in Phase 20.

---

## 20. Phase 18 report — the documentation that described a different system

Two findings, and the second is worse than the first.

**The repository had no `README.md`.** Seventeen phases, forty-five documents, a
generated API contract and a reference client, and nothing at the front door.
Anyone opening it on GitHub saw a file listing. It has one now, and what it does
*not* do is claim the project is finished: it points at `DEVELOPMENT_STATUS.md`
and says in as many words that this is the honest account and should be read
before trusting any claim made anywhere else, including in the README itself.

**`getting-started.md` still described Phase 1.** It said the Platform had no
capability modules yet — "that is by design" — that the frontend would arrive in
Phase 7, and it applied exactly one migration:

```
dotnet ef database update --context KernelDbContext
```

There are twelve. Each module owns its own schema and its own migration history
(ADR-004), which is a deliberate architectural decision that this document had
never been updated to reflect. A new developer following it word for word gets a
Platform that starts cleanly, reports healthy on `/health/ready`, and answers 500
from every single module — with no indication anywhere that eleven schemas are
missing.

That is worse than no documentation. Absent instructions send somebody to read
the code; confidently wrong instructions send them into an afternoon of debugging
a system that is behaving exactly as it should. It is now the first entry under
common problems, phrased as the symptom rather than the cause, because the
symptom is what somebody will search for.

**The same rot in the indexes.** `docs/development/README.md` listed as *planned*
several documents that exist, and carried a prerequisites table asserting that
the .NET SDK was **not installed** — true on the day it was written and false
ever since. It now lists what exists, and, for what does not, says why:
`coding-standards.md` and `testing.md` are not written because the standards are
enforced by the compiler, `dotnet format` and the architecture tests, and a prose
restatement of a mechanised rule is a second copy that drifts. `troubleshooting.md`
was folded into the two places a problem is actually met. Only
`adding-a-module.md` is a genuine gap, and it is now recorded as one (#68) rather
than listed as planned for a seventeenth phase.

**A guard, tested before being trusted.** `scripts/check-doc-links.py` fails the
build on a broken relative link. It found nothing on its first run — the links
were all good — which is exactly the shape of the two dead instruments found last
phase, so it was pointed at a deliberately broken link to confirm it could fail
at all. It caught the broken one, ignored the valid one, and correctly ignored a
link inside a fenced code block, where the integration guide shows example URLs a
caller would request rather than files on disk.

**What is still not done, and cannot be done from here.** Phase 11's acceptance
criterion asks that the integration guide be validated by an outside developer
actually following it (#40). That has not happened, and reading it back is
precisely the self-assessment the criterion rules out — checking it against the
generated contract already caught two real errors, an endpoint that did not exist
and a method that was wrong, which is evidence that reading it back is not
enough. It is now the oldest open documentation item, and it needs a person who
did not write it.

---

## 21. Phase 20 report — the tests that asked instead of assuming

Taken out of order, because it is the only remaining phase that needs nothing
from the provider decision. Six suites, one live defect, and two lessons that
cost three CI runs to learn.

**The defect first, because it was live.** Readiness had been answering **503**
whenever the document bucket was unreachable, for three phases. The composition
root registers that check with `failureStatus: Degraded`; the runbook says
Degraded; the check caught its own exception and returned
`HealthCheckResult.Unhealthy` — and a returned result overrides the registration
silently, because `failureStatus` applies only when a check *throws*. So the
Platform would have emptied every instance out of the load balancer over object
storage: the Phase 10 outage in different clothes, where a folder only Documents
needed stopped every module from starting. The lesson written down after that
outage was encoded in Phase 14 and had not been true since.

Nothing could have found it by reading. Both halves are individually correct and
they disagree only at runtime. It was found by injecting the failure, on the
first run of the test written to inject it. The check now returns
`context.Registration.FailureStatus`, so the decision exists in exactly one
place — naming a status in the check at all was a second copy, and the second
copy is what caused it.

**Declaration is not enforcement.** An architecture test has asserted since
Phase 1 that every endpoint declares a permission or explicitly allows
anonymous. That reads metadata. A middleware ordered wrongly, or a route group
mapped without `RequireAuthorization`, would leave every one of those
declarations unhonoured and the test would stay green. So the matrix now calls
**every endpoint in the running server's route table** — with no credentials,
with a structurally valid forged token, and signed in as an account that holds
nothing — and asserts 401, 401 and 403. The list comes from the route table, so
an endpoint added next year is covered the day it is mapped.

The three columns catch different mistakes. Anonymous proves authentication
runs. A forged token proves the signature is actually checked, which a pipeline
that validates nothing would also pass. A signed-in caller holding nothing
proves the *permission* is consulted — and that is the one an endpoint mapped
with a bare `RequireAuthorization()` would fail, admitting every employee in the
company.

**Two lessons about UUIDv7, learned twice.** The first forty-eight bits are a
millisecond timestamp, so the first twelve hex characters are the clock.
Truncating one to make a unique test code produces values that collide for
everything created in the same instant — which is exactly what a test method
does. That failed the company fixtures; one commit later the same mistake
appeared in an *assertion*, where `DoesNotContain` on the first eight characters
of an id matches every row in the table and therefore fails against perfectly
correct behaviour. v7 is excellent for index locality and useless truncated.

**A 415 that had to be explained rather than suppressed.** The upload endpoints
answered 415 to a JSON body, and where that 415 is produced decides whether
there is a hole: binding and the handler run *after* the authorization
middleware, so a 415 from there would mean an anonymous caller was already
through. Sending a body of the kind each endpoint declares it accepts removed
that explanation and the answer became 401 — the ordering was right all along.
The reasoning is kept in the test because it is the reusable part: a non-401
here is not automatically a false alarm.

**The sweep that deletes is now tested on what it refuses to delete.** Four of
the retention suite's seven assertions are about rows that must survive. A dead
letter is an event that will never arrive — an audit entry or a notification
permanently missing, waiting for a person — and a timer that quietly removed
those would erase the evidence of the one failure the outbox exists to make
visible. It also answered something unreadable from the code: `ExecuteDelete`
with a bound `Take` does translate on Npgsql. Had it not, the sweep would have
thrown once every six hours, the journal would have recorded a failure nobody is
watching yet, and the table would have grown for ever while the code looked
correct.

**The two module suites that were never written now exist.** Organization
(#12): a subtree move rebasing a grandchild never named in the command, a
refused cycle leaving the tree exactly as it was, the unique constraint enforced
by PostgreSQL rather than by the handler that checks first, the same code
allowed in another company — which a unique index on `code` alone would break
while passing the previous test — and the `text_pattern_ops` index actually
serving the prefix query, which is not a correctness question until the company
has four thousand units and every scope resolution starts scanning. Authorization
(#16): the join, a subject with no grants resolving to nothing rather than to
everything, a revocation taking effect on the *very next* resolution, and twelve
concurrent version bumps all counting — the last being the one only a database
can answer, since read-modify-write would collapse two simultaneous revocations
into one increment and leave a stamp some cache still matches.

**A test was written and then deleted.** A seeder assertion duplicated
`PermissionSeedingTests`, which covers it better. A second, weaker copy of an
existing test is worse than none.

**WCAG 2.2 AA, and a correction.** The phase asks for 2.2 and the suite was
running 2.1. The criteria 2.2 adds that a machine can check are about focus
being visible and unobscured and targets being large enough to hit — precisely
what a dense administrative table gets wrong. It passed unchanged, and this
report said so.

**That was true of what was tested and not of the product.** Every table in the
end-to-end environment was empty, so `target-size` had no row actions to
measure. The moment the settings seeder gave the Configuration screen four real
rows, it failed in both locales: the *Edit* and *History* buttons are around
twenty pixels tall, which is comfortable with a mouse and a genuine problem with
a thumb or a tremor. Fixed in `DataTable` rather than at each call site — every
screen writes its own buttons, and a rule repeated fourteen times is missing
from at least one of them. The design is unchanged: still text, still blue,
still no icons, simply large enough to hit.

The lesson is not about accessibility. **An empty table passes almost
everything.** Any check that only ever ran against a Platform with no data in it
has been asking a much easier question than the one it appears to ask.

**What is not done, and why it is not laziness.** The load profile exists as
executable thresholds — ARCHITECTURE.md §24's four budgets, prose for twenty
phases, now the thing k6 exits non-zero on — and it has never been run (#74),
because numbers from an empty database measure the framework rather than the
Platform. The index review (#67) needs the same realistic volume. An external
penetration test (#75) is an engagement to book, not code to write. All three
want a Phase 19 environment, and Phase 19 waits on Q4.

---

## 22. Next step

**The decision, not the code.** Everything buildable without knowing where this
runs has now been built. Q4 — which managed PostgreSQL, with what retention
window and what recovery point objective — blocks:

- **#65, nothing is taking a backup**, which is the highest-severity item in the
  register because every other risk there is survivable and this one is not. The
  audit trail cannot be reconstructed from anywhere.
- **#55, seven alert conditions** that exist as prose because there is no
  backend to create them in.
- **#74, #67, the load test and the index review**, both of which need a seeded
  environment of plausible size.
- **Phase 19 entirely.**

The Platform has been live on Railway since Phase 7, which is what makes this
easy to keep deferring. Railway is an excellent way to have something running.
It is not a decision about where the company's identity, audit trail and
documents live.

Also still outstanding:

- **B3 — the requirements document**, absent for twenty phases. Every decision
  so far has been made from ARCHITECTURE.md and the master prompt.
- **Q10 — the bootstrap administrator procedure** needs approval.
- **#40 — the integration guide has never been followed by an outside
  developer**, which was Phase 11's explicit acceptance criterion and the one
  thing self-assessment cannot satisfy.
- **#52 — nothing uses the configuration module**, including every retention
  period and interval added since Phase 9.
