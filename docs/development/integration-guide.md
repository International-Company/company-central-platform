# Integrating a system with the Platform

For a team building a business system — a financial system, an HR system, a
purchasing system — that needs the company's identity, organization, permissions,
approvals, notifications and documents.

**You do not need the Platform's source code, and you do not need the Platform
team to write anything for you.** Everything below is data you supply through the
API.

---

## 1. Get registered

Someone with `platform.applications.manage` registers your system. There is no
self-service path: registration hands out a permission namespace and the ability
to hold roles, so a human with authority does it and the act is audited.

Ask for:

| Thing | Example | Notes |
|---|---|---|
| A code | `finance` | Your permission namespace. Permanent. |
| A name | `Financial System` | For the administration screens. |
| A credential | | You receive a client id and a secret. |

The secret is **shown once**. It is stored hashed, cannot be retrieved, and
cannot be resent. Losing it means being issued another and revoking the first.

Put it wherever your deployment keeps secrets. Not in your repository — the
Platform will not stop you, and a secret scanner will find it before an attacker
does only if you are lucky.

---

## 2. Get a token

```http
POST /api/v1/oauth/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials
&client_id=ccp_MtQ2…
&client_secret=ccps_9jK1…
```

```json
{
  "access_token": "eyJhbGciOiJSUzI1NiIs…",
  "token_type": "Bearer",
  "expires_in": 900
}
```

Then send it as `Authorization: Bearer <token>` on every call.

**Cache it.** The token is good for its whole lifetime and asking for a new one
per request will hit the token endpoint's rate limit, which is deliberately
tight — that endpoint is where somebody guesses at your secret. Ask for a new one
when the old one is close to expiring.

### Validating a token yourself

Tokens are RS256-signed. Fetch the public keys once from
`/api/v1/.well-known/jwks.json` and validate locally — the Platform does not need
to be called to check a token, which is what keeps it from being a synchronous
dependency of every request in your system.

---

## 3. Acting for a person

Some work is yours. Some is somebody's, and should be recorded as theirs.

```
grant_type=client_credentials
&client_id=…
&client_secret=…
&on_behalf_of=8f3a2b1c-…
```

The resulting token names both of you: the person as the subject, your
application alongside. Audit records **which application, on behalf of whom**.

Two things gate it, and both are deliberate:

- Your application must hold `platform.applications.act-on-behalf`. Being allowed
  to read employees does not make you allowed to read them *as* the finance
  director.
- **The call can do only what your application may do *and* what that person may
  do.** The narrower of the two wins, scope included. An application limited to
  one department, acting for somebody with company-wide access, still reaches one
  department.

Use it when a person triggered the work. Use a plain machine token for a nightly
job that is nobody's action in particular.

---

## 4. Declare your permissions

Your system decides what its own permissions are. The Platform stores and
evaluates them without knowing what they mean.

```http
PUT /api/v1/applications/permissions
Authorization: Bearer <your machine token>
Content-Type: application/json

{
  "permissions": [
    { "name": "finance.invoices.view",    "description": "See invoices" },
    { "name": "finance.invoices.approve", "description": "Approve an invoice" }
  ]
}
```

**There is no application in the path or the body.** The namespace comes from
your token, so you can only ever declare your own — the restriction is
structural rather than a check somebody has to remember. It also means a person's
token cannot call this at all: a manifest is your system's statement about
itself.

Rules worth knowing before you write the list:

- Every name must start with **your** code. `finance.*` and nothing else — this
  is what stops one system granting itself access to another's resources.
- The manifest is **complete and idempotent**. Send the whole list every time
  your system starts; the Platform reconciles. Incremental add-and-remove calls
  drift the moment one fails, and nobody notices until a permission check does
  the wrong thing in production.
- Names you stop declaring are **deactivated, not deleted**. Roles still
  reference them and audit records from last year still name them.

Administrators then put your permissions into roles and grant those roles to
people, in the Platform's own screens. You write no user interface for any of it.

---

## 5. Ask whether somebody may do something

```http
POST /api/v1/authorization/check
Content-Type: application/json

{ "permission": "finance.invoices.approve", "userId": "8f3a2b1c-…" }
```

The answer is not a boolean. It carries the **scope**: which records that person
may act on, as organizational path prefixes your query can apply directly.

That distinction matters more than it looks. "May Sara approve invoices?" is
almost never the real question — "which invoices may Sara approve?" is, and a
yes/no answer leaves your system to guess.

---

## 6. What else is worth using before building it yourself

| You need | Use | Instead of |
|---|---|---|
| Who works here, and who reports to whom | `/api/v1/employees`, `/api/v1/organization/units` | Your own employee table |
| Approvals | Workflow — register a definition, start an instance | Your own state machine |
| Telling somebody something | Notifications — a template and a send | Your own mail code |
| Storing a file against your records | Documents — upload, then link `resourceType`/`resourceId` | Your own blob storage |
| Recording what happened | Audit — post an event under your namespace | Your own log table |

Each of these is the reason the Platform exists. A business system that
reimplements them ends up with a second employee directory that disagrees with
the first, which is the specific failure this whole project is built to prevent.

### Workflow, in one paragraph

Register a definition — steps, who approves each one, what the transitions are —
as data, through the API. Start an instance against your own record
(`purchase-order` / `PO-2026-0041`). The engine assigns tasks, escalates what
goes stale, and raises events when things complete. **It holds no business rule
of yours**: routing that depends on an amount or a category is decided by your
system, which passes the resulting assignees in when it starts the instance.

### Documents, in one paragraph

Upload the file, then link the document to your record. Retrieving what is filed
against a record is one call, and it returns what the *caller* may see — so two
people opening the same order can correctly see different numbers of documents.

---

## 7. Failures

Every failure is an RFC 9457 problem document with a stable `code`:

```json
{
  "code": "AUTHZ.PERMISSION_DENIED",
  "correlationId": "01JBQ…",
  "errors": [ { "code": "…", "message": "…", "field": "…" } ]
}
```

**Match on `code`.** Messages are translated into Arabic and English and reworded
freely; codes are part of the contract and are not changed for a condition that
already has one.

**Keep the `correlationId`.** It retrieves the log line, the trace and the audit
record for that exact request. When you report a problem, quoting it is the
difference between an answer in minutes and an afternoon of guessing.

| Status | Means |
|---|---|
| 401 | No token, an expired one, or bad client credentials |
| 403 | Authenticated, and not allowed — check the permission the contract names |
| 404 | Not there, or not yours to see |
| 409 | A conflict with the current state |
| 422 | The request was well formed and the content was refused |
| 429 | Rate limited. `Retry-After` says when to come back |

---

## 8. Rate limits

Your application has its own budget — a thousand requests a minute by default,
separate from any person's. A batch job cannot exhaust the allowance of the
employees it acts for, and one integration cannot slow the Platform down for
everybody else.

The token endpoint is much tighter, keyed on your client id. If you are near it,
you are not caching your token.

Respect `Retry-After` on a 429. Retrying immediately is refused again and makes
the backlog worse.

---

## 9. Rotating your secret

Rotation is designed to be boring, and boring is the point — a rotation that
risks an outage is a rotation nobody performs.

1. Ask for a second credential. Your application may hold **two live secrets at
   once**.
2. Deploy the new one at whatever pace your fleet allows. Both work.
3. Watch `lastUsedAt` on the old credential in the applications screen. When
   nothing has used it for long enough to be sure, revoke it.

Revocation is immediate — no grace period, no cache, no next-refresh. Tokens
already issued keep working until they expire, which is minutes; nothing new can
be obtained.

---

## 10. Before you go live

- [ ] The secret is in your secret store, not in your repository.
- [ ] Tokens are cached and refreshed before expiry, not per request.
- [ ] Unknown JSON fields are ignored, not treated as errors.
- [ ] Error handling matches on `code`, not on message text.
- [ ] Unknown enumeration values do not throw.
- [ ] `correlationId` is logged with every failure.
- [ ] 429 is honoured with a back-off.
- [ ] Your permission manifest is sent on startup and is complete.
- [ ] `Sunset` headers are logged when they appear.
- [ ] You have asked for the narrowest roles that let your system work.

---

## 11. Where to look next

| For | Read |
|---|---|
| The full contract | `contracts/platform-api.json` |
| What may change inside a version | [`docs/api/versioning.md`](../api/versioning.md) |
| Workflow | [`docs/workflow/README.md`](../workflow/README.md) |
| Notifications | [`docs/notifications/README.md`](../notifications/README.md) |
| Documents | [`docs/documents/README.md`](../documents/README.md) |
| A working example | [`samples/reference-client/`](../../samples/reference-client/) |
