# Two database roles

Reference: [ARCHITECTURE.md §21](../../ARCHITECTURE.md) (Deployment), [§15.4](../../ARCHITECTURE.md) (Audit).

**Changing the schema and serving requests are different privileges.** Migrations
need the right to create and drop tables. Handling a request needs the right to
read and write rows. Until now the Platform did both as one role, which meant:

- A SQL-injection defect anywhere in the Platform is a defect that can drop a
  table, not merely read one.
- The audit trail's append-only guarantee is a `REVOKE`, not a promise — and the
  role bound by it could grant it straight back, from the same connection.

Neither of those is a defect anybody has found. They are consequences a role
separation removes outright, which is a better position than not having the
defect yet.

---

## 1. What the Platform needs from you

Two connection strings and one setting:

```
CCP_ConnectionStrings__Platform=Host=...;Database=ccp_platform;Username=ccp_app;Password=...
CCP_ConnectionStrings__PlatformMigrations=Host=...;Database=ccp_platform;Username=ccp_migrator;Password=...
CCP_Database__ApplicationRole=ccp_app
```

`PlatformMigrations` is **optional**. A deployment that supplies only `Platform`
runs exactly as it always has, on one role — which is what keeps this from being
a change every environment has to make on the same day.

`Database:ApplicationRole` is the one that is easy to forget and expensive to
forget. See §3.

`DATABASE_URL` is deliberately **not** a fallback for the migration string. That
variable is the one credential a hosting platform publishes, and it is the
application's. Falling back to it would silently reunite the two roles in exactly
the deployment that had gone to the trouble of separating them, while the
configuration went on reading as though they were apart.

---

## 2. Creating the roles

```bash
psql "$SUPERUSER_URL" \
  -v app_password="'$(openssl rand -base64 24)'" \
  -v migrator_password="'$(openssl rand -base64 24)'" \
  -f scripts/database-roles.sql
```

The script is idempotent and prints what each role ended up holding. Read the
output: `ccp_app` must appear with no `CREATE` anywhere, and with no `UPDATE` or
`DELETE` on `audit`.

It creates the schemas itself, owned by `ccp_migrator`. That is deliberate — a
schema created by a superuser and then written to by the migrator leaves the
migrator unable to alter what it created, and the failure arrives during a
migration rather than during setup.

Passwords are passed in and never written down here. Nothing in this repository
holds a credential ([secrets-management.md](../security/secrets-management.md)).

---

## 3. The part that fails silently

The audit trail is append-only because a migration issues `REVOKE ALL` and then
`GRANT INSERT, SELECT`. A migration has to name the role it is binding, and with
one role the answer was `current_user` — obviously right, and right for as long
as there was one role.

**Once the roles are separated, `current_user` during a migration is the
migrator.** The append-only grant would land on the role that never writes an
audit event, and the application would be left not append-only but *unable to
append*. Nothing says so until the first audited action in production.

So the role is told to the migration rather than assumed by it: the migrator
publishes `Database:ApplicationRole` as the PostgreSQL session setting
`ccp.application_role`, and the migration reads it, falling back to
`current_user` when it is absent. Two integration tests run the shipped
migration's own SQL — read from the migration object rather than transcribed —
and ask PostgreSQL what the role ended up holding, in both the configured and the
unconfigured case.

If you separate the roles and do not set `Database:ApplicationRole`, the
migration logs a warning and the application cannot write audit events.

---

## 4. Adding a table later

Nothing to do. `scripts/database-roles.sql` sets `ALTER DEFAULT PRIVILEGES FOR
ROLE ccp_migrator`, so a table the next migration creates is readable and
writable by `ccp_app` the moment it exists. Without that, every deployment that
added a table would be followed by an application that could not read it, and the
failure would arrive after the release rather than during it.

Re-running the script after adding a **schema** is still required; the schema list
is in the script.

---

## 5. What this does not do

- **It does not stop a migration from being wrong.** It stops a running request
  from being able to issue one.
- **It does not restrict the migrator.** The migrator is a powerful role by
  necessity; what changes is that it is used for minutes during a deployment
  rather than continuously by everything the Platform serves.
- **It is not a substitute for parameterised queries.** Every query in the
  Platform goes through EF Core or an explicitly parameterised command. This is
  the second layer, and the reason to have a second layer is that the first one
  only has to fail once.
