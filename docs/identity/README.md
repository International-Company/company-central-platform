# Identity & Organization Documentation

Reference: [ARCHITECTURE.md §7.2.1](../../ARCHITECTURE.md) (Identity), §7.2.2 (Organization), §13 (Authentication).

## Planned documents

| Document | Phase |
|---|---|
| `users.md` — account lifecycle, profile, states | Phase 2 |
| `sessions-and-devices.md` | Phase 2 |
| `login-history.md` | Phase 2 |
| `organization.md` — company, departments, sections, centers, positions | Phase 3 |
| `employees.md` — the employee record and its link to a user account | Phase 3 |
| `bootstrap-administrator.md` — the one-time, audited creation procedure | Phase 4 |

## Boundary reminder

An employee record here is an **organizational** fact: who works here, in which unit, reporting to whom. Salary, leave, attendance and appraisals belong to the HR business application, which references these employees by ID. See [ADR-005](../architecture/adr/ADR-005-platform-business-boundary.md).

