# Bootstrap Administrator

Creating the first account on an empty Platform.

| | |
|---|---|
| Status | **Mechanism built. Operational procedure needs the project owner — open question Q10.** |
| Reference | [ARCHITECTURE.md §7.2.3](../../ARCHITECTURE.md) · [ADR-012](../architecture/adr/ADR-012-application-registry.md) |

---

## 1. The problem

Every system needs one account that exists before any account can be
administered. That account is a standing risk: it is the one credential nobody
granted, created outside the normal process, often with a weak password chosen
in a hurry and shared over chat.

A hardcoded default administrator is how systems get breached on day one. The
mechanism here is built so that cannot happen.

---

## 2. What protects it

| Protection | Effect |
|---|---|
| **Refuses to run if any user exists** | Can only ever create the *first* account. It is not a back door into a running system. |
| **Off by default** | Enabling it is an explicit act, not a side effect of a config file being present. |
| **No defaults** | Missing username, email or password is a fatal startup error. The Platform will not invent an administrator. |
| **Policy applies** | The initial password goes through the same policy as any other. A bootstrap account is not exempt — it is the account that most needs the rules. |
| **Must change at first sign-in** | The handover credential stops working the moment the real administrator uses it. |
| **Audited and logged at warning level** | The creation raises a normal `UserCreatedEvent`, and logs prominently so it does not blend into startup noise. |

---

## 3. Configuration

```
Identity:Bootstrap:Enabled          true            (temporarily)
Identity:Bootstrap:Username         <chosen>
Identity:Bootstrap:Email            <chosen>
Identity:Bootstrap:DisplayName      <chosen>
Identity:Bootstrap:InitialPassword  <from the secret manager>
```

As environment variables:

```
CCP_Identity__Bootstrap__Enabled=true
CCP_Identity__Bootstrap__Username=...
CCP_Identity__Bootstrap__Email=...
CCP_Identity__Bootstrap__InitialPassword=...
```

> **The initial password is a secret.** It comes from the cloud secret manager
> or an environment variable, never from a committed file (ARCHITECTURE.md
> §12.7). It must satisfy the full password policy.

---

## 4. Procedure — proposed, pending Q10

This is the recommendation. **It is not yet approved**, because Q10 — who the
bootstrap administrator is — has not been answered.

1. **Decide who**, in advance and in writing. This account will be able to grant
   every permission in the company.
2. **Generate the initial password** with a password manager, at least 20
   characters. Do not choose it by hand and do not reuse anything.
3. **Store it in the secret manager**, not in chat, email or a ticket.
4. **With the owner present**, set the bootstrap configuration and start the
   Platform.
5. **Confirm the warning log line** naming the created account, and confirm the
   `UserCreatedEvent` reached the audit trail.
6. **Sign in immediately** as that account and change the password. The initial
   one stops working at that moment.
7. **Disable bootstrapping** (`Identity:Bootstrap:Enabled=false`) and **delete
   the initial password** from configuration and from the secret manager.
8. **Record the date, the account and who was present.**

Steps 6–7 matter most. Between creation and first sign-in, the initial password
is a live administrator credential.

---

## 5. Open questions

| Q | Question | Needed for |
|---|---|---|
| **Q10** | Who is the bootstrap administrator, and is this procedure approved? | Any real deployment |
| — | Should the account be a named person or a break-glass account used once and disabled? | Recommendation below |

**Recommendation.** Use it once to create the real named administrator accounts,
then **disable the bootstrap account**. A shared administrator account that
stays in use is unattributable: the audit trail records that "admin" did
something, which is not evidence about any person. Named accounts are what make
the audit trail worth having.

---

## 6. What happens without it

Nothing. An empty Platform with bootstrapping disabled has no users and no way
to create one through the API, because creating a user requires a permission and
permissions require an account. That is deliberate — the Platform does not open
a hole to be convenient.

---

## 7. Recovery

If the last administrator account is lost, there is currently **no recovery
path**: bootstrapping refuses to run once users exist. This is intentional but
it is also a real operational risk.

The mitigations are procedural rather than technical, and belong to Phase 4 when
roles exist:

- More than one person holds the administrator role.
- The break-glass procedure is documented in `docs/deployment/`.
- The database backup can be restored, but that loses everything since the last
  one.

**This should be revisited in Phase 4**, when the authorization model can express
"more than one administrator" properly.
