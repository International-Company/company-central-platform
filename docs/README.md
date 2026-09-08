# Company Central Platform — Documentation

| Status | Phase 0 — structure created; content written phase by phase |
|---|---|
| Last updated | 2026-09-06 |

## Start here

| Document | What it is |
|---|---|
| [ARCHITECTURE.md](../ARCHITECTURE.md) | The architecture: scope, boundaries, modules, and every technical layer |
| [PROJECT_PLAN.md](../PROJECT_PLAN.md) | The 22 phases, each with objective, tasks, tests and acceptance criteria |
| [DEVELOPMENT_STATUS.md](../DEVELOPMENT_STATUS.md) | Where the project actually stands, including blockers and risks |
| [architecture/adr/](architecture/adr/) | Why each significant decision was made |

## Areas

| Directory | Contents | Written in |
|---|---|---|
| [architecture/](architecture/) | Architecture detail and decision records | Phase 0 onward |
| [security/](security/) | Threat model, controls, secrets, hardening | Phases 2, 5 |
| [identity/](identity/) | Users, sessions, devices, organization structure | Phases 2, 3 |
| [authorization/](authorization/) | Permissions, roles, scope, integration for consumers | Phase 4 |
| [audit/](audit/) | The event model and how a system sends events | Phase 6 |
| [workflow/](workflow/) | Defining and running approval workflows | Phase 8 |
| [notifications/](notifications/) | Templates, channels, providers | Phase 9 |
| [documents/](documents/) | Upload, access control, storage | Phase 10 |
| [integrations/](integrations/) | Connectors, credentials, webhooks | Phase 12 |
| [api/](api/) | REST conventions, versioning, errors, OpenAPI | Phases 1, 11 |
| [development/](development/) | Getting started, standards, integration guides | Phase 1 onward |
| [deployment/](deployment/) | Environments, CI/CD, observability, backup, recovery | Phases 17, 19 |

## For a developer building a business system

The question this documentation must answer is: **"How do I connect my system to the Company Platform?"** — without reading Platform source code.

The path, once written (Phases 11 and 18):
1. `development/integration-guide.md` — register an application and make a first authenticated call
2. `authorization/integration.md` — declare permissions and check them
3. `audit/integration.md` — send audit events
4. `workflow/integration.md`, `notifications/integration.md`, `documents/integration.md` — use the shared services
5. `api/` — conventions, errors, versioning

## Writing rules

- Documentation is updated **within** the phase that changes the system, never deferred.
- If the documentation and the code disagree, that is a defect in one of them — find out which and fix it.
- No secret, credential or real personal data appears in any document or example.
- Examples must be runnable. An example that has never been run is a guess.

