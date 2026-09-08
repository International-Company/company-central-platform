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
