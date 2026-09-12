# Organization

The company's structure, and where people sit in it.

| | |
|---|---|
| Status | Phase 3 |
| Reference | [ARCHITECTURE.md §7.2.2](../../ARCHITECTURE.md) · [ADR-005](../architecture/adr/ADR-005-platform-business-boundary.md) |

---

## 1. What this module is for

One authoritative answer to "who works here, in which unit, reporting to whom".
Every business system needs it, and none of them should keep its own copy.

It is **not** an HR system. See §6.

---

## 2. Departments, sections and centers are one entity

The brief names them separately. Modelling them as three tables would be the
literal reading, and the wrong one.

| Three tables | One `OrganizationUnit` with a type |
|---|---|
| Every hierarchy query becomes a union of three | One query |
| "Everything under this unit" spans three tables | One indexed prefix scan |
| A fourth kind of unit needs a schema change | An enum value |
| Nesting depth is baked into the schema | Arbitrary |

The brief's own requirement — that the structure be extensible and assume no
fixed number of departments or centers — is better served by one entity than by
encoding today's three levels into the schema.

Types available: `Department`, `Section`, `Center`, `Division`, `Branch`, `Team`.
If the company ever needs to define its own, promoting the enum to a reference
table is a contained change. That is the escalation path, not the starting
point (P1).

---

## 3. The hierarchy

**Adjacency list plus materialized path.**

```
units
  id          parent_id   path                          depth
  ─────────── ─────────── ───────────────────────────── ─────
  hq          NULL        /hq/                          0
  finance     hq          /hq/finance/                  1
  payable     finance     /hq/finance/payable/          2
```

(Real paths use 32-character hex ids; shortened here for readability.)

`parent_id` is what edits need. `path` is what reads need:

```sql
-- everything under finance, as one indexed range scan
SELECT * FROM organization.units WHERE path LIKE '/hq/finance/%';
```

### Why this matters more than it looks

From Phase 4, authorization scope (`Unit`, `UnitAndBelow`) is resolved on **every
request** (ADR-007 §14.3). A recursive query per request would put a tree walk on
the hot path of every call in the company. A prefix scan does not.

The index is declared with `text_pattern_ops` deliberately — the default
collation's operator class cannot serve a `LIKE 'prefix%'` query, so without it
the index would exist and never be used.

### The leading and trailing separators

Both are load-bearing. Without the trailing `/`, the path of unit `ab` would
prefix-match unit `abc`, and a scope check would silently include a unit it
should not. There is a test for exactly this.

### The cost

A move rewrites the path of every descendant. That is a rare operation against a
frequent read, so the trade is the right way round — but it must be **atomic**.
A partial rewrite leaves units claiming an ancestry they no longer have, and
authorization then resolves against a tree that does not exist.

`MoveUnitHandler` loads the descendants **before** the move (afterwards the
prefix no longer matches), rebases them all, and commits in one `SaveChanges`.

---

## 4. Cycles

Two kinds, prevented differently.

**Unit hierarchy** — moving a unit beneath its own descendant would detach the
whole branch from the root. Caught by a single path comparison, because the
materialized path already encodes the ancestry.

**Reporting lines** — no materialized path, because manager changes are far more
common than unit moves and maintaining one would mean rewriting a chain on every
change. So the check walks the chain instead, which is cheap because chains are
short.

Reporting cycles matter because the workflow engine walks this chain to find an
approver (ARCHITECTURE.md §16.3). A loop would hang that walk or route an
approval to the wrong person. The walk also terminates on a repeat, so data that
is already corrupt degrades into a truncated answer rather than a hang.

---

## 5. Bilingual names

Every name is stored in Arabic and English, in two columns — not a JSON blob, so
both are indexable and visible to anyone querying the database directly.

**Both are required.** An optional second language becomes a permanently empty
column: whoever creates the record is in a hurry, nobody comes back, and the
Arabic interface ends up showing English names. That is exactly the second-class
experience ADR-011 exists to prevent.

The API returns both, so a language toggle in the UI needs no round trip.

---

## 6. The boundary — what belongs to HR

This is the sharpest boundary in the Platform (ADR-005 §4.3a).

| Platform (here) | HR application |
|---|---|
| Who works here | What they are paid |
| Which unit they belong to | Leave taken and remaining |
| Who they report to | Attendance |
| Their position | Appraisals |
| Employee number, work contact | Contract terms, benefits |
| Active or not | Payroll history |

The HR system references these employees **by id**. It does not copy them, and
the Platform does not hold its data.

> The pressure to add "just one field" here will be constant and will always
> sound reasonable. Salary is the canonical example: it is obviously about an
> employee, and admitting it would put payroll rules in the Platform within a
> year. Apply the ARCHITECTURE.md §4.4 test to every field proposed for
> `Employee`.

### The pressure valve: a typed extension bag

That test says no to almost everything, and it should. But an application
genuinely does need somewhere to keep its own note about a person — a cost
centre, a badge number, a shift pattern — and without one the pressure goes
somewhere worse: a column on `Employee` that the Platform then has to understand.

```http
PUT    /api/v1/organization/employees/{id}/attributes/payroll.cost-centre
{ "value": "CC-1180" }

GET    /api/v1/organization/employees/{id}/attributes
DELETE /api/v1/organization/employees/{id}/attributes/payroll.cost-centre
```

**Namespaced, and that is the load-bearing part.** A key is
`<application>.<name>`, so two business systems that both care about "status" do
not collide — and an attribute's owner can be read off the key rather than
looked up. A key with no namespace belongs to nobody, and the first collision
would be silent: one application overwriting another's value, both convinced they
owned it. The database enforces one value per key per employee, so two
applications racing settle in PostgreSQL rather than by luck.

**A credential pasted here is refused.** An employee record is exported, backed
up and broadly readable inside the company, so a secret put here is a secret in
all of those places — and the person who put it there did so because it was
convenient, which is exactly when it happens. The check is the one the
Configuration module applies to a setting, and it now lives in the kernel because
the argument was never about settings.

**Fifty per employee.** A bag with no limit is a table somebody eventually uses
as a database, and this row is one a company has to be able to describe in full
when somebody asks what is held about them. Replacing an existing attribute
always works, even at the limit — a limit that blocked edits would strand an
application with fifty attributes it could never correct.

**Reading returns everything, from every application.** "What do you hold about
me" is a question that must be answerable in full, and an endpoint that only ever
showed one application's slice would make the complete answer something only a
database query could produce.

Setting and removing are audited by **key, never by value**: an attribute's value
is an application's business and may be somebody's medical category, while "who
decided this" is the question actually being asked.

What this is **not** is a way around §6. An attribute is an application's own
note, opaque to the Platform, which reads it back and never interprets it. The
moment the Platform needs to understand a field, it is a Platform field and the
§4.4 test applies.

---

## 7. Employees and user accounts

The link is **optional in both directions**:

- An employee with no account — a worker who needs no system access.
- An account with no employee — a service account, a contractor, an
  administrator who is not on the payroll.

`Employee.UserId` is a plain column with **no foreign key**. Identity is a
different module in a different schema, and a key across that boundary would
make both unextractable (ADR-004 §10.2). Integrity is enforced by the use case,
which checks through Identity's contract, plus a unique partial index ensuring
one employee per account.

---

## 8. Endpoints

| Method | Path | Permission |
|---|---|---|
| GET | `/api/v1/organization/units/tree` | `platform.organization.view` |
| POST | `/api/v1/organization/units` | `platform.organization.manage` |
| PUT | `/api/v1/organization/units/{id}/name` | `platform.organization.manage` |
| POST | `/api/v1/organization/units/{id}/move` | `platform.organization.manage` |
| POST | `/api/v1/organization/units/{id}/deactivate` | `platform.organization.manage` |
| GET | `/api/v1/organization/employees` | `platform.employees.view` |
| POST | `/api/v1/organization/employees` | `platform.employees.manage` |
| POST | `/api/v1/organization/employees/{id}/transfer` | `platform.employees.manage` |
| PUT | `/api/v1/organization/employees/{id}/user` | `platform.employees.manage` |
| `GET` | `/organization/employees/{id}/attributes` | `platform.employees.view` |
| `PUT` | `/organization/employees/{id}/attributes/{key}` | `platform.employees.manage` |
| `DELETE` | `/organization/employees/{id}/attributes/{key}` | `platform.employees.manage` |

Reading the structure is broadly permitted; changing it is not. The people who
may see the org chart are many; the people who may restructure it are few.

> **These permissions are declared and not yet enforced.** The handler that
> evaluates them arrives in Phase 4. Until then these endpoints require
> authentication only.

### Scoping a query to a subtree

```
GET /api/v1/organization/employees?unitId={id}&includeSubUnits=true
```

The same prefix-scan primitive authorization scope will use — proving here that
"this unit and everything below" is one indexed query.

---

## 9. Deactivation, not deletion

Units, positions and employees are deactivated. Nothing is deleted.

An employee record from three years ago is referenced by business documents,
audit entries and approval histories across every company system. Deleting the
row would orphan all of them.

A unit cannot be deactivated while it still has active children or active
employees — that would leave people attached to something the organization no
longer considers real, and their scope would resolve against a dead branch.

---

## 10. Events

| Event | Consumers should |
|---|---|
| `organization.unit.created` | — |
| `organization.unit.moved` | **Invalidate cached scope** for the unit and every descendant |
| `organization.unit.deactivated` | — |
| `organization.employee.hired` | — |
| `organization.employee.transferred` | **Invalidate cached scope** for that person |
| `organization.employee.manager_changed` | Re-resolve any pending approval routing |
| `organization.employee.user_link_changed` | Re-resolve the user-to-employee mapping |
| `organization.employee.deactivated` | — |

The two marked invalidating matter from Phase 4: a `Unit` grant follows the
employee record and the unit path, so a move or a transfer changes what someone
can see. A stale cache there grants access to the wrong part of the company.

---

## 11. What is not built

| Missing | Where it went |
|---|---|
| `EmployeeAssignment` history | Deferred. The current unit, position and manager are on the employee. Historical assignment tracking is genuinely useful for "where was this person in March?" but nobody has asked for it (P1). |
| Matrix / dotted-line reporting | A single manager, because workflow's approver resolution must be unambiguous. A separate entity is the escalation path. |
