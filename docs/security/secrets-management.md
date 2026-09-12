# Secrets Management

Reference: [ARCHITECTURE.md §12.7](../../ARCHITECTURE.md), [ADR-009](../architecture/adr/ADR-009-cloud-strategy.md).

---

## 1. The rule

> **ممنوع تخزين الأسرار داخل Git أو Source Code**
> No secret is stored in Git or in source code.

Not in configuration, not in a test fixture, not in a comment, not in an example,
not "temporarily". This covers passwords, API keys, JWT signing keys, database
passwords and any private credential.

The rule is absolute because the failure is irreversible: a secret committed to
Git is in every clone, every fork and every backup, and removing it from history
does not remove it from the machines that already pulled. The only correct
response to a committed secret is to rotate it, which is expensive and often
noticed late.

---

## 2. How the Platform holds secrets instead

**Every secret enters through the environment, and the values that would be
secrets are supplied as *paths* wherever the secret is a key.**

| Secret | Setting | Form |
|---|---|---|
| Database connection | `CCP_ConnectionStrings__Platform` | Value, from the secret manager |
| JWT signing key | `CCP_Jwt__SigningKeyPath` | **Path** to a mounted key file |
| MFA protection key | `CCP_Security__MfaProtection__KeyPath` | **Path** to a mounted key file |
| Object storage credentials | `CCP_Storage__AccessKey`, `CCP_Storage__SecretKey` | Value, from the secret manager |

The `CCP_` prefix keeps Platform settings distinct from unrelated environment
variables on a shared host.

A path rather than a value, for key material, because it keeps the key out of the
process environment — where it would be visible to anything that can read
`/proc`, appear in crash dumps and container inspection output, and be logged by
diagnostics that dump configuration.

---

## 3. Per environment

| Environment | Source |
|---|---|
| Development | `.env`, gitignored. `.env.example` is committed and contains **placeholders only**. |
| CI | Repository secrets, injected as environment variables. Never echoed. |
| Staging / Production | Cloud secret manager, mounted or injected at deploy time. Never in an image, never in a manifest committed to the repository. |

`.env.example` marks values that must be replaced with
`REPLACE_WITH_LOCAL_PATH_NOT_COMMITTED`. Anything still carrying that marker at
startup is treated as absent.

---

## 4. Fail loudly, never invent

Outside Development, a missing key is a **startup failure with a message naming
the setting**, not a generated fallback.

This matters more than it looks. If the Platform generated an MFA protection key
when none was configured, the key would differ on every instance and change on
every deployment — so every enrolled second factor would silently become
undecryptable, and every user would be locked out with no explanation and no
obvious cause. A crash on startup is recoverable in minutes. Silent key drift is
discovered days later, by users who cannot log in.

In Development only, an ephemeral MFA key is generated with a warning that says
plainly that enrolments will not survive a restart.

---

## 5. Rotation

| Secret | Rotation |
|---|---|
| JWT signing key | JWKS publishes the current key by `kid`; a new key is added and the old one retained until every issued token has expired, then removed. Access tokens are short-lived, so the overlap is minutes. |
| MFA protection key | A new key is made active under a new id and the old one is listed as retired; secrets written under either are readable throughout, and each is rewritten under the new key the next time its owner verifies. See §5.1. |
| Database password | Rotated in the secret manager; the deployment is restarted to pick it up. |

### 5.1 Rotating the MFA protection key

Until recently this could not be done at all. A stored secret was bare base64 of
`nonce | tag | ciphertext` with nothing to say which key wrote it, so a new key
made every enrolled second factor undecryptable — which is to say, locked every
user out of their own account. **A key that can never be replaced is a key that
stays in place after the laptop it was generated on is sold.**

The stored form is now `v2.{keyId}.{base64(nonce | tag | ciphertext)}`. The
version lives in a prefix rather than in the encrypted bytes, and that is what
made the change deployable: a value written before versioning has no prefix, is
recognised by its absence, and is read with the active key — which is the only
key it can have been written with.

To rotate:

1. Generate a new key: `openssl rand -base64 32`.
2. Put it where the active key lives (`Security:MfaProtection:KeyPath`, or
   `:Key` on a platform with no mounted files).
3. Give it a new id: `Security:MfaProtection:ActiveKeyId`. Any short label with
   no full stop — `2`, `2026-09`, whatever the procedure finds meaningful. The
   full stop separates the fields of the stored form, and one in a key id would
   write values that cannot be read back; the Platform refuses to start rather
   than let that happen.
4. List the old key as retired, under **the id it was active with**:

   ```
   Security__MfaProtection__RetiredKeys__0__Id=1
   Security__MfaProtection__RetiredKeys__0__KeyPath=/run/secrets/mfa-2025.key
   ```

5. Deploy. New enrolments use the new key immediately; existing secrets are
   still read with the retired one.

**The retired key must stay configured until every secret written under it has
been rewritten.** Removing it early denies the second factor to everyone whose
secret still needs it — which is the outage this whole mechanism exists to make
avoidable, arrived at by a different route.

A secret naming a key the deployment does not hold is **denied**, not guessed
at. Trying every key in turn would turn a configuration mistake into a silent
success under the wrong assumption, and there is no good version of that.

### 5.2 When the old key can be removed

Secrets move themselves. On a successful TOTP verification the plaintext is
already in hand, so a secret written under a retired key — or under no named key
at all, which is everything stored before versioning existed — is re-encrypted
under the active key and saved with the rest of that verification. Nobody is
asked to do anything, and nothing is logged: the secret has not changed, only the
key protecting it, and an audit entry here would be a line about housekeeping in
a trail people read to find out who did what.

Two things it deliberately does not do:

- **A recovery code does not move a secret.** It never decrypts one. Somebody who
  signs in only with recovery codes stays on the old key, so a rotation is not
  finished merely because everybody has verified *something*.
- **A disabled enrolment is left alone.** Rewriting a secret nobody can use would
  carry a dead factor forward onto every future key.

So the old key comes out when the secrets under it are gone, not on a date:

```sql
-- 1 = pending, 2 = active; 3 = disabled, which nobody can use and which the
-- rewrite deliberately skips. A pending enrolment counts: confirming it reads
-- the secret too, so removing its key would strand somebody mid-enrolment.
select count(*) from security.mfa_enrolments
where status in (1, 2) and encrypted_secret not like 'v2.<new-key-id>.%';
```

At zero, remove the retired key from configuration and deploy. Until then it
stays, and the only people it is still holding open for are those who have not
signed in with their authenticator since the rotation. **There is no pass that
forces this to finish** — a dormant account can sit on the old key indefinitely,
and the query above is what says so rather than a guess about how long is long
enough. Resetting such a user's factor (`POST /security/users/{id}/mfa/reset`)
ends it for that account.

---

## 6. Verification

- `.gitignore` excludes `.env`, `*.key`, `*.pem`.
- CI runs secret scanning and fails the build on a detected credential.
- No test fixture contains a real credential; test keys are generated at runtime
  into temporary files and deleted afterwards.
