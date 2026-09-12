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
| MFA protection key | A new key is made active under a new id and the old one is listed as retired; secrets written under either are readable throughout. See §5.1. **Re-encryption of existing secrets is still manual** — until each is rewritten, the retired key must stay configured. |
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
been rewritten.** Removing it early denies the second factor to everyone who has
not re-enrolled since — which is the outage this whole mechanism exists to make
avoidable, arrived at by a different route.

A secret naming a key the deployment does not hold is **denied**, not guessed
at. Trying every key in turn would turn a configuration mistake into a silent
success under the wrong assumption, and there is no good version of that.

**What is not built: an automatic re-encryption pass.** Today a secret moves to
the new key only when the person re-enrols. Rewriting each one on its next
successful use would finish a rotation without anybody being asked to do
anything, and is recorded as debt rather than implied here.

---

## 6. Verification

- `.gitignore` excludes `.env`, `*.key`, `*.pem`.
- CI runs secret scanning and fails the build on a detected credential.
- No test fixture contains a real credential; test keys are generated at runtime
  into temporary files and deleted afterwards.
