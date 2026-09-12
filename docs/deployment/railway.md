# Deploying to Railway

Two services and a database, in one project.

| Service | Builds | Serves |
|---|---|---|
| `company-central-platform` | `build/api.Dockerfile` | The Platform API |
| `ccp-frontend` | `frontend/Dockerfile` | The administration interface |
| `Postgres` | — | The database, one instance for every module schema |

---

## Why there is no `railway.json`

There was one, at the repository root, and it caused the frontend service to
deploy the backend.

A service reads the config file from its root directory. Both services have the
repository as their root, so both read the same file — and that file named
`build/api.Dockerfile`. The frontend service dutifully built and shipped the
API.

`RAILWAY_DOCKERFILE_PATH` did not rescue it either: the config file takes
precedence over the variable.

**One config file cannot describe two different services.** Each is configured
with its own variables instead, which is explicit about the thing that actually
differs between them.

---

## Service variables

### `company-central-platform` (the API)

```
RAILWAY_DOCKERFILE_PATH          = build/api.Dockerfile
DATABASE_URL                     = ${{Postgres.DATABASE_URL}}
CCP_Database__ApplyMigrationsOnStartup = true
CCP_Identity__SigningKey         = <base64 of a PEM RSA private key, 3072-bit>
CCP_Security__MfaProtection__Key = <base64 of a 256-bit key>
CCP_Identity__Issuer             = https://<the API's public domain>
CCP_Identity__Audience           = ccp-api
```

`DATABASE_URL` is a reference, not a literal. Railway does not share a
database's variables with other services automatically, and the first deployment
crash-looped on exactly that.

Generate the keys with:

```bash
openssl genrsa 3072 | base64 -w0     # signing key
openssl rand -base64 32              # MFA protection key
```

Both are supplied as values rather than mounted files because Railway offers no
mounted files. A path is better and remains preferred wherever one exists — see
`docs/security/secrets-management.md`.

#### Document storage

Nothing here is required for the service to start, and **that is a trap worth
naming.** With no object storage configured the Documents module falls back to a
directory inside the container, which a Railway deployment discards on every
restart. Files uploaded on Tuesday are gone on Wednesday, and nothing fails
loudly when it happens.

So a deployment that will hold real documents sets these:

```
CCP_Documents__S3__BucketName       = ccp-documents
CCP_Documents__S3__ServiceUrl       = <endpoint, for anything that is not AWS>
CCP_Documents__S3__Region           = <region>
CCP_Documents__S3__AccessKeyId      = <key id>
CCP_Documents__S3__SecretAccessKey  = <secret>
```

Any S3-compatible service will do — Cloudflare R2, Backblaze B2, DigitalOcean
Spaces, MinIO, or AWS itself. The module uses object storage when it is
configured and the local directory when it is not; it does not decide from the
environment name, because "production means S3" works right up until the first
production deployment without a bucket.

**The bucket must not be publicly readable.** Every download is authorized by
the Platform and served either as a short-lived pre-signed URL or as a stream; a
public bucket makes every access rule in the module decorative.

### `ccp-frontend`

```
RAILWAY_DOCKERFILE_PATH = frontend/Dockerfile
PLATFORM_API_URL        = https://<the API's public domain>
NODE_ENV                = production
```

The frontend holds no Platform secret at all. It signs in through the API like
any other caller and keeps the tokens in an httpOnly cookie the browser cannot
read.

---

## The first administrator

Set these five, deploy once, sign in, change the password, then **remove all
five**:

```
CCP_Identity__Bootstrap__Enabled         = true
CCP_Identity__Bootstrap__Username        = admin
CCP_Identity__Bootstrap__Email           = <an address you can reach>
CCP_Identity__Bootstrap__DisplayName     = Administrator
CCP_Identity__Bootstrap__InitialPassword = <a long one-time passphrase>
```

The account is created with `MustChangePassword`, so the initial password is a
handover credential and stops working the moment the real administrator signs
in. The seeder refuses to run once any user exists, so it can only ever create
the first account — but leaving the password in configuration afterwards is
leaving a credential lying about for no reason.

---

## Both images build from the repository root

One context, one set of ignore rules. `frontend/.next` and
`frontend/node_modules` are excluded: large, reproducible, and belonging in
neither image.

---

## Backups: an unanswered question, not a deferred one

**Nothing in this repository records whether this database has backups.**

The long-term answer is blocked on Q4 — continuous archiving and point-in-time
recovery are features of a managed PostgreSQL, and which managed PostgreSQL is
not decided. `backup-and-recovery.md` holds the procedure and
`scripts/verify-restore.sh` holds the checks, and neither has run against a real
dump (DEVELOPMENT_STATUS.md §7, debt #65 and #66).

None of that is a reason not to know the answer for the database that is running
right now. Railway offers backups on its PostgreSQL plans; whether they are
switched on here, how often they run and how long they are kept is a question
somebody can answer from the Railway dashboard this afternoon, without deciding
anything about Q4. **Write the answer here when you have it**, including if the
answer is no — "nobody has checked" and "there are none" call for different
actions, and today only the first is true.

This matters more than its place at the end of a deployment guide suggests. The
audit trail is the one thing in the Platform that cannot be reconstructed from
anywhere else: every other table could in principle be rebuilt from the business
systems that fed it, and that one is the record of who did what. Losing it is
not an outage.
