# Authentication

How a person or a system proves who they are to the Company Central Platform.

| | |
|---|---|
| Status | Phase 2 |
| Reference | [ADR-006](../architecture/adr/ADR-006-authentication-strategy.md) · [ARCHITECTURE.md §13](../../ARCHITECTURE.md) |

---

## 1. The shape of it

```
Browser ──► Next.js BFF ──► Platform API
            (holds tokens)   (issues them)

Business app ──────────────► Platform API
              (holds its own token)
```

Two kinds of caller, authenticated differently because the threats differ.

**People** never hold a token. The Next.js BFF signs in on their behalf, keeps
the tokens server-side, and gives the browser an `httpOnly` session cookie. This
removes token theft via XSS as a category — there is nothing in the page for a
script to steal.

**Business applications** hold their own tokens and validate Platform tokens
locally against the published JWKS, so the Platform is not a synchronous
dependency of every request in the company.

---

## 2. Tokens

| | Access token | Refresh token |
|---|---|---|
| Form | JWT, RS256 | Opaque, 256 random bits |
| Lifetime | 15 minutes | 14 days, sliding |
| Stored | Not stored | **Hashed** (SHA-256) |
| Revocable | No — expiry is its only revocation | Yes, immediately |
| Rotates | — | On every use |

### Why the access token is short

It cannot be revoked. Whatever its lifetime is, that is how long a leaked token
keeps working, so the lifetime *is* the revocation delay. Fifteen minutes is the
compromise between that and hammering the refresh endpoint.

### Why refresh tokens are opaque, not JWTs

A refresh token means nothing on its own — it is a lookup key into server-side
state. That is exactly what makes it revocable, which a self-contained JWT is
not.

### Why they are hashed with SHA-256, not Argon2id

The input is 256 bits of uniform randomness. There is no low-entropy guess space
for an attacker to search, so the memory-hard hashing that passwords need would
only add latency to every refresh. The reasoning that makes Argon2id right for
passwords is what makes it unnecessary here.

### Why signing is asymmetric

With a shared symmetric secret, every business application that could *validate*
a token could also *mint* one. RS256 means consumers hold only the public key.

---

## 3. Rotation and reuse detection

This is the most valuable control in the design.

```
sign-in ──► token A
              │  refresh
              ▼
            token B  (A is now spent)
              │  refresh
              ▼
            token C  (B is now spent)

  someone presents A again
              │
              ▼
   ENTIRE FAMILY REVOKED + security event
```

**Why it matters.** A refresh token is a long-lived bearer credential. If it is
copied, both the thief and the real user hold something valid, and nothing in a
non-rotating design tells them apart — the compromise is silent and lasts until
expiry.

With rotation, whichever party refreshes second presents a spent token. That one
observation converts an undetectable compromise into a detected incident. The
real user is signed out and must authenticate again, which is a small price for
closing the theft.

**The whole family is revoked, not just the replayed token**, because the thief
may already have rotated several times. Revoking one token would leave them
holding a valid successor.

---

## 4. Sign-in

```
POST /api/v1/auth/login
{ "username": "ahmad", "password": "…", "deviceFingerprint": "optional" }
```

Success returns an access token, a refresh token, the expiry, and the user's
profile. Failure **always** returns the same thing:

```json
{ "status": 401, "code": "IDENTITY.INVALID_CREDENTIALS",
  "detail": "The username or password is incorrect." }
```

### What is uniform, and why

| Situation | Response |
|---|---|
| Unknown username | `IDENTITY.INVALID_CREDENTIALS` |
| Wrong password | `IDENTITY.INVALID_CREDENTIALS` |
| Account disabled | `IDENTITY.INVALID_CREDENTIALS` |
| Account locked | `IDENTITY.INVALID_CREDENTIALS` |
| No credential set | `IDENTITY.INVALID_CREDENTIALS` |

Any difference lets an anonymous caller discover who works at the company. The
real reason is recorded in login history and on the outbox, where operators can
see it and attackers cannot.

**Timing is uniform too.** When the username does not exist, the handler still
performs a hash verification against a dummy hash computed once at startup.
Without it an unknown username returns in microseconds and a real one in about
100 ms — and that gap alone enumerates the directory, whatever the response body
says.

**Account state is checked *after* the password is verified**, for the same
reason: checking "is this account disabled" first would reveal by timing that
the account exists.

---

## 5. Lockout

Progressive, not permanent.

| Consecutive failures | Delay |
|---|---|
| 1–3 | none |
| 4 | 30 seconds |
| 5 | 1 minute |
| 6 | 2 minutes |
| … | doubling |
| capped at | 30 minutes |

A hard lock after N failures is the common design and a poor one: it hands an
attacker a denial-of-service tool against any user whose username they can guess.
A growing delay makes credential stuffing impractical while leaving a real user
only briefly inconvenienced.

A lapsed lockout is simply not a lockout — no background job is needed to clear
it, which is one fewer thing that can fail.

---

## 6. Sessions

A session records the user, device, address, creation time and last activity. It
ends when any of these is true: the user signs out, an administrator revokes it,
the password changes, the account is disabled, reuse is detected, the idle
timeout passes (12 hours), or the absolute lifetime passes (30 days).

The absolute ceiling matters: without it, continuous use extends a session
forever.

**Signing out revokes server-side.** Deleting a cookie is not signing out — a
refresh token copied beforehand would still work.

---

## 7. Passwords

Argon2id. Memory-hard, so the work cannot be made cheap with GPUs or ASICs,
which is how modern offline cracking works.

Default cost: 64 MiB, 3 iterations, 2 lanes. Too low and cracking is cheap; too
high and an authentication burst becomes a self-inflicted denial of service,
because each attempt reserves that memory.

**The Platform measures this itself, at startup, on the hardware it is actually
running on**, and writes the figure to the log:

```
Password hashing takes 143ms on this hardware with m=65536KiB t=3 p=2.
```

Below 100 ms it says so as a warning, because cheap for the Platform is cheap for
somebody working through a stolen password table — which is the entire thing
these parameters exist to make expensive. Above a second it says so too, more
mildly: safe and slow is a smaller problem than fast.

**It reports and does not tune.** Hashing cost is a security parameter, and a
Platform that raised its own would change how long every sign-in takes on a
schedule nobody chose, with no record of what it used to be — and a machine that
happened to be busy during startup would pick a number wrong for every hour
after. The measurement is one hash, once per process, and the decision stays with
whoever owns the deployment.

Raise it with `Identity:Argon2:MemoryKib` until the figure in the log is one you
are happy with under load.

Hashes are self-describing: `$argon2id$v=19$m=65536,t=3,p=2$<salt>$<hash>`. The
parameters travel with the hash, so raising the cost later does not invalidate
anyone's password — existing credentials are upgraded transparently at sign-in,
the one moment the plaintext is known.

### Policy

| Rule | Value |
|---|---|
| Minimum length | 12 |
| Maximum length | 256 |
| History | last 5 refused |
| Breach screening | on (see §8) |
| Must not contain username | yes |
| Expiry | **off** |

There are deliberately **no composition rules** — no "must contain an uppercase
letter and a symbol". Those push people toward `Password1!` and toward writing
passwords down, while adding very little real entropy. Current guidance (NIST SP
800-63B) is to require length, screen against breaches, and otherwise leave the
choice alone.

Expiry is off for the same reason: forced rotation produces weaker, more
predictable passwords. Rotate on evidence of compromise.

The maximum length is not a security limit. Argon2id cost scales with input, so
an unbounded password is a cheap way to make the server do expensive work.

---

## 8. Breach screening

Uses k-anonymity: the password is hashed with SHA-1 and only the **first five
hex characters** are sent. The service returns every known-breached hash suffix
sharing that prefix, and the match is made locally. The service never receives
the password, its full hash, or enough to identify it.

SHA-1 here is a corpus lookup key, not a security measure — it has nothing to do
with how passwords are stored.

**It fails open.** If the service is slow or unreachable, the password is
allowed and the failure is logged. Being unable to create or recover an account
because a third party is having an outage is worse than the screening gap.

**It is off by default** (`Identity:BreachedPasswords:UseExternalService`).
Enabling an outbound call to a third party is the project owner's decision, not
a default. **While it is off, no screening happens at all** — this is stated
plainly so nobody believes a control is running when it is not.

---

## 9. Password reset

```
POST /api/v1/auth/password/forgot   { "email": "…" }   →  202 Accepted, always
POST /api/v1/auth/password/reset    { "token": "…", "newPassword": "…" }
```

The forgot endpoint **always returns 202**, whether the address belongs to an
account, belongs to a disabled account, is malformed, or is empty. It is
anonymous, so any observable difference would make it a free tool for
discovering the company's address list.

The reset token is a full account-takeover credential while it lives, and is
treated as one: stored hashed, single use, 30-minute lifetime, and superseded
whenever a newer one is issued or the password changes by another route — so a
stale link in an old inbox cannot be redeemed.

A successful reset ends **every** session, because whatever prompted it may have
been a compromise.

> **Not yet delivered.** The Notifications module (Phase 9) is what actually
> sends the email. Until then the event is recorded on the outbox and no message
> goes out. Reset is therefore **not usable end to end yet**.

---

## 10. Changing a password

```
POST /api/v1/auth/password/change   { "currentPassword": "…", "newPassword": "…" }
```

The current password is required even though the caller is already
authenticated. That is the point: a stolen access token should not be enough to
take an account permanently.

Success ends the user's **other** sessions, keeping the one they are using. If
the old password was compromised, the sessions it opened may be too.

---

## 11. Token signing keys

`Identity:SigningKeyPath` names a PEM file. **The key material never appears in
configuration or in the repository** — the file comes from the secret manager or
a mounted secret.

In Development only, and only when no path is set, an ephemeral key is generated
in memory so the Platform runs immediately after cloning. It lives for the life
of the process: restarting invalidates every token, which is exactly what makes
it unusable in production by accident.

**Outside Development a missing key is a fatal startup error.** Silently
generating one would mean tokens that survive no deployment.

Public keys are published at:

```
GET /api/v1/.well-known/jwks.json
```

Anonymous by necessity and by design — a business application fetches these
before it holds any credential, and they are public keys.

> **Key rotation has not been exercised.** The `kid` mechanism supports it, but
> the procedure has not been written or rehearsed. It must be, in staging,
> before go-live — not at go-live.

---

## 12. What is not built yet

| Missing | Phase |
|---|---|
| MFA / TOTP | 5 |
| Per-endpoint rate limits on auth | 5 |
| Permission enforcement (the strings exist; nothing evaluates them) | 4 |
| Reset emails actually being sent | 9 |
| External SSO federation | Open question Q5 |
| Key rotation procedure | 5 |

> **Operational warning.** Rate limiting is currently global, not per-endpoint.
> The global limit is far too generous for a login endpoint. **Do not expose
> this to the internet before Phase 5.**
