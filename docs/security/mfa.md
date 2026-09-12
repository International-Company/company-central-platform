# Two-Factor Authentication

Reference: [ARCHITECTURE.md §12.5](../../ARCHITECTURE.md), [ADR-006](../architecture/adr/ADR-006-authentication-strategy.md).
Introduced in Phase 5.

---

## 1. What was chosen, and why

**TOTP (RFC 6238) as the only factor, for now.**

| Option | Why not now |
|---|---|
| SMS | Costs money per message, and SIM swapping is a routine attack rather than a theoretical one. A factor an attacker can obtain from a phone shop is not a second factor. |
| Email | Usually protected by the same password being defended. Not independent. |
| WebAuthn / hardware keys | Genuinely better — phishing-resistant in a way TOTP is not. Requires hardware the company does not yet have. Not ruled out, and not seamed for either: see §11. |
| **TOTP** | **Free, offline, no provider dependency, supported by every authenticator app. Good enough now, and it does not block WebAuthn later.** |

TOTP is not phishing-resistant: a convincing fake login page can collect a code
and replay it within thirty seconds. That is a known and accepted limitation of
this phase, recorded here rather than left for someone to discover.

The algorithm is implemented in `Totp.cs` rather than taken from a package. It is
about thirty lines, it is verified against the RFC 6238 test vectors, and a
third-party dependency sitting in the authentication path is a supply-chain risk
disproportionate to what it saves.

---

## 2. Enrolment

```
POST /api/v1/me/mfa/enrol      → { provisioningUri, manualEntryKey }
POST /api/v1/me/mfa/confirm    → { codes[], count }        # recovery codes
GET  /api/v1/me/mfa            → status, no secret ever
POST /api/v1/me/mfa/verify     → verification + step-up elevation
POST /api/v1/me/mfa/disable    → requires the current code
```

**Enrolment starts pending and only becomes a factor once proven.** Activating on
issue would lock out anyone whose QR scan failed or whose phone clock is wrong,
with no way back in. Starting a second enrolment discards the pending one — that
is the recovery path for a scan that went wrong.

**The secret is returned exactly once.** `GET /me/mfa` never returns it, and there
is no endpoint that does. Re-showing it would let anyone holding a stolen session
clone the factor, which would make the factor worth nothing.

**No permission is required to protect your own account.** Requiring one would
let an administrator prevent people from securing their accounts.

---

## 3. Recovery codes

Ten codes, ten characters each from a 25-character alphabet — about 46 bits, far
beyond guessing and still short enough to write down. The alphabet deliberately
excludes `0/O`, `1/I/L`, `2/Z`, `5/S` and `8/B`: a code that is correct but
unreadable is a support call.

Stored **hashed with SHA-256**, not Argon2id. The same reasoning as refresh
tokens: the input is high-entropy random rather than human-chosen, so there is no
small guess space to search and memory-hard hashing would only add latency.
Argon2id protects against guessing what a person would choose; nobody guesses 46
bits of randomness.

Codes are **single use**, **shown once**, and **invalidated when MFA is disabled**
— a code outliving the factor it belonged to would be a standing bypass for an
account that no longer expects one. Regenerating retires the previous set, so a
printout from a year ago stops working the moment the user believes they have
replaced it.

Using a recovery code is recorded at **Medium** severity rather than
Informational. It means the usual factor was unavailable, which is either a lost
phone or someone else's hands.

---

## 4. Secret storage

TOTP secrets are **encrypted, not hashed**. A password is verified by hashing the
candidate, so the original is never needed; computing an expected TOTP code
requires the secret itself. That is a genuinely weaker position than password
storage, and the mitigation is that the key lives outside the database.

- **AES-256-GCM**, stored as `nonce | tag | ciphertext`, base64.
- A **fresh random nonce per encryption**. Nonce reuse with GCM does not merely
  weaken it — it reveals the key stream and allows forgery.
- GCM rather than CBC because it authenticates as well as encrypts: tampered
  ciphertext fails to decrypt instead of silently yielding a wrong secret.
- Decryption failure returns null, and the caller's response is always the same:
  deny the factor. A wrong key and a tampered ciphertext are indistinguishable
  from outside and need no distinction.

**The key is supplied as a file path, never as a value.**
`CCP_Security__MfaProtection__KeyPath` points at a file holding a base64-encoded
256-bit key, mounted from the secret manager. Outside Development the Platform
**refuses to start** without it. Generating one silently would be worse than
failing: every enrolled second factor would become undecryptable on the next
deployment and every user would be locked out with no explanation.

In Development only, an ephemeral key is generated with a loud warning. Enrolments
do not survive a restart, which is correct for a development machine.

A leaked database backup therefore yields ciphertext, not working second factors.

---

## 5. Step-up authentication

**MFA is enforced at the privileged action, not at the door.**

The Platform does not refuse sign-in to an administrator who has no second
factor. Doing so would lock people out of the system they need in order to
*enrol* one, and would punish people for a policy change made while they were
away. Instead, `[RequireStepUp]` demands a recent confirmation at the point where
it matters. An administrator with no MFA can sign in and read; they cannot act.

Enforcing at the action also catches the case the door cannot see: a session
stolen *after* a legitimate sign-in. The token is valid, the session is real, and
step-up still stops it.

### Two refusals, not one

Both come back `403`, because a prober must not learn which it is. The body says
which, because the person in front of the screen needs to know.

| Code | Means | What the portal does |
|---|---|---|
| `SECURITY.STEP_UP_REQUIRED` | You have a second factor and your elevation has lapsed. | Opens the confirmation dialog and retries the action once you pass. |
| `SECURITY.MFA_REQUIRED_BY_POLICY` | You have no second factor at all. | Says so, and sends you to enrol. |

**The Platform said the first to both for as long as step-up has existed.**
Somebody who had never enrolled was told to "verify your second factor and
retry", and the portal opened a dialog asking for a code they could not produce.
The way out was a screen nothing had sent them to. The error for the second case
had been written, with a comment describing the distinction as though it were
implemented, and nothing ever raised it.

The enrolment is only looked up once the elevation check has already failed, so
the ordinary path — a request from somebody whose elevation is valid — costs
nothing extra.

### Where it applies

| Endpoint | Why |
|---|---|
| `POST /users` | A new account is a new way in, one the intruder sets the password of. |
| `POST /users/{id}/enable` | Restores a way in that something deliberately closed. |
| `POST /users/{id}/disable` | How an intruder removes the person who would notice. |
| `POST /users/{id}/unlock` | Undoes a lockout that was doing its job. |
| `POST /users/{id}/roles` | Granting a role is how access is created — the most consequential action in the Platform. |
| `DELETE /users/{id}/roles/{assignmentId}` | Stripping an administrator's access is how an intruder buys time. |

This set is pinned by `StepUpCoverageTests` **in both directions**. An endpoint
that loses its step-up requirement fails the build; so does one that gains an
unreviewed one. A test that only checked a minimum would let elevation spread
until people started working around it.

Organization and employee management deliberately do **not** require step-up.
They are routine daily work for HR administrators, and a fifteen-minute
re-prompt through a day of data entry is the kind of friction that gets a
security control disabled.

### How it works

`[RequireStepUp]` accompanies `[RequirePermission]`; it never replaces it.
Permission answers *may this person do this at all*; step-up answers *are they
still here, right now*. An endpoint carrying only step-up would admit any user
who had proven a code — which is every enrolled employee. This too is enforced by
a test.

Elevation lasts **fifteen minutes**: long enough for a run of administrative work
without constant re-confirmation, short enough that a walked-away-from session
cannot be used for what step-up protects. The window is **absolute, not sliding**
— a sliding window would keep an abandoned session elevated indefinitely as long
as it kept being used.

**Elevation is held in the database, not in the token.** A step-up claim inside
an access token could not be revoked before the token expired, and prompt
revocation is most of its value. It is also bound to the **session**, not just
the user: a confirmation on a laptop must not privilege a stolen token from a
different browser.

Disabling MFA revokes every outstanding elevation. Otherwise turning off the
factor would leave up to fifteen minutes of privileged access standing on
nothing.

The MFA endpoints themselves never require step-up — needing elevation to enrol
the factor that grants elevation is a deadlock nobody could escape. A test
enforces that too.

---

## 6. Attempt limits

Ten failed verifications lock the enrolment. A six-digit code with three accepted
periods gives roughly three chances in a million per attempt, so ten keeps
guessing negligible while tolerating a phone whose clock is off.

The drift window is **one period either side** — thirty seconds each way. Zero
tolerance produces constant spurious failures; every additional period triples
the codes valid at any instant, which is the attacker's surface. One is the
balance.

Verification compares in **constant time** and does **not** break out of the
window loop on a match. Returning early would make a current-period code verify
measurably faster than a previous-period one, leaking which period matched.

Every MFA endpoint carries the strict authentication rate limit (10/minute),
because they all accept codes.

---

## 7. Security events

MFA activity is written to `security.security_events`, separately from the audit
trail. Audit answers *who changed what*; security events answer *what is being
attempted*. The two have different readers, different retention and different
urgency.

**Events are committed independently of the operation that produced them.** The
recorder holds its own `DbContext` for exactly this reason: a failed sign-in
rolls back everything else it touched, and the event recording that failure must
survive the rollback. Otherwise the only attempts on record would be the
successful ones — precisely backwards for detection.

The table carries **no foreign key to `identity.users`**, and not only because
foreign keys do not cross schema boundaries. Events must be able to record
attempts against usernames that do not exist, and those rows — a null `user_id`
and an unknown username — are exactly what reveals credential stuffing.

`GET /api/v1/security/events` requires `platform.security.view` and searches
within a **bounded time window**, defaulting to the last seven days. An unbounded
query over a table that grows with every failed sign-in is a scan, and someone
opening a dashboard should not be able to cause one.

---

## 8. Known limitations

| Limitation | Status |
|---|---|
| TOTP is not phishing-resistant. | Accepted. WebAuthn is the answer, and **there is no extension point for it** — this row used to say there was. `MfaEnrolment` stores a shared secret and carries no method discriminator, and the endpoints, DTOs and handlers name TOTP directly; adding WebAuthn means a discriminator, a different kind of stored material (credential id, public key, signature counter) and its own endpoints. Building that seam against a method nobody has the hardware for would shape it around this one anyway — but a plan made on the strength of the old sentence would have been a plan against nothing. |
| MFA is not required to sign in, only to act. | Deliberate — see §5. Revisit if a business application needs read protection too. |
| ~~No administrator-initiated MFA reset for a user who lost both phone and codes.~~ | **Built.** `POST /api/v1/security/users/{userId}/mfa/reset`, gated on `platform.security.manage`, requiring a written reason and audited. This row described it as a gap for several phases after it shipped. |
| ~~Nothing in this module has run against a real database.~~ | **False as written, and it contradicted its own second half.** `MfaFlowTests` runs against PostgreSQL 17 on every push. What is true is narrower and belongs to the machine, not the module: debt #70, no local PostgreSQL superuser password and no Docker here, so the suite is verified in CI rather than before the push. |
