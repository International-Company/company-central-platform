# Authorization

Who may do what, to which records.

| | |
|---|---|
| Status | Phase 4 |
| Reference | [ADR-007](../architecture/adr/ADR-007-authorization-strategy.md) · [ADR-012](../architecture/adr/ADR-012-application-registry.md) · [ARCHITECTURE.md §14](../../ARCHITECTURE.md) |

---

## 1. The model in one page

```
Application ──owns──► Permission
                          ▲
                          │
                      RolePermission
                          │
                          ▼
User ──UserRoleAssignment──► Role
         (+ scope, + expiry)
```

- **Permissions** are named `<application>.<resource>.<action>`.
- **Roles** bundle permissions. Everything flows through roles.
- **Assignments** grant a role to a user *at a scope*, optionally until a date.

**Direct user-permission grants do not exist.** They are always convenient in
the moment, and they are the reason that years later nobody can answer "why does
this person have access to that?". A user's access is the union of their roles,
visible in one screen.

---

## 2. Permission names

```
platform.users.view          the Platform's own
finance.invoices.approve     the Financial system's
hr.leave-requests.view       the HR system's
```

**This is how the Platform stays business-agnostic while being the authorization
authority for business systems.** The Platform owns `platform.*` and nothing
else. A registered application owns its namespace, declares permissions in it,
and the Platform stores and evaluates them **without understanding what they
mean**. To it, `finance.invoices.approve` is an opaque string attached to a role.

That resolves the apparent contradiction in the brief — know nothing about
business systems, be the single place their permissions live — because the
Platform never interprets the string.

An application declaring a permission outside its own namespace is rejected.
Without that check, a registered business system could declare
`platform.users.create` and grant itself the Platform's administrative rights.

---

## 3. Scope

"View employees" is meaningless without asking **which** employees.

| Scope | Reaches |
|---|---|
| `Self` | Only the holder's own records |
| `Unit` | One organizational unit |
| `UnitAndBelow` | A unit and everything beneath it |
| `All` | Company-wide |

Every value is **organizational, never business-conditional**. There is no "only
records under 5,000" — that would be a business rule, and it would put the
Platform back inside the boundary ADR-005 draws.

### The anchor

A unit scope is either **anchored** to a specific unit, or **follows the
holder**:

```
anchor = null       → "your own department, wherever you are"
anchor = <unitId>   → "the Gaza branch", regardless of where you work
```

Without the anchor, delegating oversight of one part of the company would
require moving the person into it.

### It resolves through the materialized path

`UnitAndBelow` is a prefix scan on the path Phase 3 built — one indexed range
scan, not a tree walk. That matters because **scope resolves on every request**.

### The failure mode that is deliberate

A holder-relative scope on a caller with **no employee record** — a service
account, a contractor — grants **nothing**. Treating it as `All` would be
catastrophic; treating it as `Self` would silently widen it. Denying is the only
safe default, and it is tested.

---

## 4. A decision is a filter, not a boolean

```csharp
AccessDecision decision = await resolver.EvaluateAsync(userId, "platform.employees.view");

decision.IsGranted          // may they, at all
decision.Scope              // how far
decision.UnitPathPrefixes   // WHICH records
```

An authorization system that answers only yes or no relies on every caller
remembering to apply the right restriction afterwards. Eventually one will not,
and it will return the whole company's records to someone entitled to see one
department. Handing back the filter makes the restriction *the answer* rather
than an obligation.

The decision travels on the request (`HttpContext.Items`), so the endpoint
applies it without resolving a second time.

---

## 5. Enforcement

```csharp
.WithMetadata(new RequirePermissionAttribute("platform.users.create"))
```

The attribute implements `IAuthorizeData`, producing the policy name
`permission:platform.users.create`. A policy provider turns that back into a
requirement, and a handler evaluates it.

**Policies cannot be registered in advance** — a business application declares
permissions the Platform has never heard of, so the set is only known at
runtime. Encoding the permission into the policy name and building the policy on
first use is what lets `finance.invoices.approve` work with no Platform code
change.

| Situation | Answer |
|---|---|
| Not authenticated | **401** — left unhandled, so the framework asks who you are |
| Authenticated, lacks permission | **403** — and the denial is recorded |
| Authenticated, holds permission | Admitted, with the filter attached |
| Authenticated, no usable subject claim | **403** — fails closed |

Denials are recorded because one is noise and a burst across many permissions
from one caller is someone mapping what they can reach.

---

## 6. Anti-escalation

Three rules, each because without it the ability to grant something becomes the
ability to grant anything.

| Rule | Without it |
|---|---|
| **No user may grant a permission they do not hold** | A junior administrator with `roles.assign` hands themselves every permission in the company via a second account |
| **No user may grant a scope wider than their own** | Department-level access grants company-wide access — the same escalation wearing a different hat |
| **No user may grant or revoke their own roles** | Anyone reaching the endpoint escalates freely; anyone under investigation tidies their own access away |

---

## 7. Caching, and why revocation is immediate

Resolving permissions is a join across assignments, roles and permissions. It
happens on every request, so it is cached — but a cache that can serve revoked
access is worse than no cache.

**A version stamp, in the database.** Every change to roles, role permissions,
grants or the organizational tree bumps it. Every cached set carries the stamp it
was computed at. A set whose stamp no longer matches is discarded.

```
request → read stamp (one indexed row)
        → stamp matches cache?  use it
        → else                  recompute and cache
```

**Not a TTL.** A TTL leaves a window — however short — in which someone keeps
access that was deliberately taken away, and "however short" is not a property
anyone can reason about during an incident.

**Not in memory.** With several instances, an in-memory counter would let
instance B keep serving access that instance A revoked.

The cost is one small read per request instead of the full join. Correctness is
exact: **revoking a role takes effect on the very next request.**

---

## 8. How a business system integrates

1. **Register the application** — it gets a namespace.
2. **Declare permissions** — send the whole manifest; it is idempotent and
   reconciles. Permissions no longer declared are *deactivated*, not deleted,
   because assignments and audit records still reference them.
3. **Check access** — `POST /api/v1/authorization/check`, which returns the
   decision *and* the filter to apply to your own data.

The manifest is declarative on purpose. Incremental add-and-remove calls drift
the moment one fails, and nobody notices until a permission check fails in
production.

---

## 9. The Platform's own permissions are derived, not listed

Every endpoint already declares its permission. The seeder **reads that
metadata** at startup and reconciles the permission table against it.

A hand-maintained manifest would be correct the day it was written and wrong
within a month: someone adds an endpoint, forgets the list, and the permission
cannot be granted because it does not exist. Deriving it makes drift impossible.

The seeder also keeps the `platform-administrator` system role holding every
active Platform permission — otherwise a newly added endpoint's permission would
be ungrantable, since granting requires holding it.

---

## 10. Endpoints

| Method | Path | Access |
|---|---|---|
| GET | `/api/v1/me/permissions` | authenticated |
| POST | `/api/v1/authorization/check` | authenticated (someone else's needs `platform.authorization.inspect`) |
| GET | `/api/v1/roles` | `platform.roles.view` |
| GET | `/api/v1/roles/permissions` | `platform.permissions.view` |
| GET | `/api/v1/users/{id}/roles` | `platform.roles.view` |
| POST | `/api/v1/users/{id}/roles` | `platform.roles.assign` |
| DELETE | `/api/v1/users/{id}/roles/{assignmentId}` | `platform.roles.assign` |

`/me/permissions` is for the frontend to hide controls the user cannot use. That
is **UX, not security** — the backend enforces independently, and a hidden button
is not a protected operation.

---

## 11. What is not built

| Missing | Note |
|---|---|
| Role create/edit endpoints | Roles can be read and granted; creating one is currently a seeding concern |
| Application registration endpoints | The domain, repository and declaration handler exist; the endpoints do not |
| Machine-to-machine credentials | OAuth2 client credentials for applications — Phase 11 |
| Scope filters on the remaining list endpoints | Employee search and the user list both apply the filter at the data layer. See §11.1 for what a scope means when the thing being listed is an account. |
| Integration tests | Nothing has run against a real database |
| ABAC | Explicitly deferred (ADR-007). Adopting it requires a superseding ADR. |

### The filter in practice

```
GET /api/v1/organization/employees
```

1. The handler evaluates `platform.employees.view` and admits the caller.
2. It attaches a `ScopeFilter` to the request.
3. The endpoint reads it and passes the reachable paths to the query.
4. The query restricts to units matching those prefixes — **at the data layer**,
   so a caller entitled to one department never has the rest of the company in
   memory.

A restriction with no reachable paths returns an empty page rather than falling
through to unrestricted. That distinction is the difference between an empty
result and the whole company.

The filter type lives in the kernel, not in Authorization: every module's
endpoints apply it, and passing Authorization's own type would make Identity and
Organization reference its internals, which §6.2 forbids.

### 11.1 What a scope means for a user account

An account has no department. **The person behind it does**, through an employee
record the Organization module owns — so a department-scoped caller sees the
accounts of the people in their department, and Identity never grows a unit
column of its own. A second place the answer lives is a second place it goes
stale.

Three consequences, each decided rather than fallen into:

- **An account with no employee record is invisible to a scoped caller.** It has
  no place in the organization, so it is in no department. The alternative —
  showing unplaced accounts to everybody — would show every service account to
  every unit administrator in the company.
- **The cost is real and worth stating:** a unit-scoped administrator who creates
  an account cannot see it until it is linked to an employee, which is the act
  that puts it in the organization.
- **No reachable units means nobody, not everybody.** The natural mistake is to
  treat an empty prefix list as "no filter", which turns a scope that grants
  nothing into a scope that grants the whole company — silently, and in the
  direction nobody notices.

Mechanically it is a list of account ids rather than a join, because no foreign
key crosses a schema boundary: Identity asks its own application-layer seam,
which the infrastructure layer answers through Organization's contract. A company
of ten thousand produces a ten-thousand-id filter, and that is the price of a
module that can be lifted out.

The filter is applied **before** paging. Narrowing a page after reading it gives
the caller a short page, a wrong total, and page numbers describing rows they are
not allowed to know exist.
