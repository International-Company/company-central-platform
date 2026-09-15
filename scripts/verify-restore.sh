#!/usr/bin/env bash
#
# Verifies that a backup is actually usable.
#
# A dump file that restores without error is not a backup. This restores into a
# scratch database and asserts the things that have to be true for a company to
# come back — not that the file parsed, but that the audit trail is still
# partitioned, that somebody can still sign in, and that no module schema went
# missing.
#
# Usage:
#   scripts/verify-restore.sh <dump-file> [admin-connection-string]
#
# The connection string is where the scratch database is created. It needs
# CREATE DATABASE rights and nothing else, and it must not be production. It is
# a libpq URI -- postgresql://user:pass@host:port/db -- not Npgsql's key-value
# form, because psql and pg_restore are the things reading it.
#
# Exit code 0 means the backup is usable. Anything else means it is not, and
# that is an incident rather than a warning.
set -euo pipefail

DUMP="${1:?Usage: verify-restore.sh <dump-file> [admin-connection-string]}"
ADMIN="${2:-${CCP_RESTORE_CHECK_POSTGRES:-postgresql://postgres@localhost:5432/postgres}}"

if [[ ! -f "$DUMP" ]]; then
  echo "No such dump file: $DUMP" >&2
  exit 2
fi

SCRATCH="ccp_restore_check_$(date -u +%Y%m%d%H%M%S)_$$"

# The scratch database is dropped whatever happens, including on a failed
# assertion. Leaving restored copies of production data lying about on whatever
# machine ran the check is its own incident.
cleanup() {
  psql "$ADMIN" -v ON_ERROR_STOP=1 -qtAc "DROP DATABASE IF EXISTS \"$SCRATCH\" WITH (FORCE);" >/dev/null 2>&1 || true
}
trap cleanup EXIT

failures=0

check() {
  local description="$1"
  local expected="$2"
  local query="$3"
  local actual

  actual="$(psql "$TARGET" -v ON_ERROR_STOP=1 -qtAc "$query" | tr -d '[:space:]')"

  if [[ "$actual" == "$expected" ]]; then
    printf '  ok    %s\n' "$description"
  else
    printf '  FAIL  %s (expected %s, got %s)\n' "$description" "$expected" "$actual"
    failures=$((failures + 1))
  fi
}

check_at_least() {
  local description="$1"
  local minimum="$2"
  local query="$3"
  local actual

  actual="$(psql "$TARGET" -v ON_ERROR_STOP=1 -qtAc "$query" | tr -d '[:space:]')"

  if [[ "$actual" -ge "$minimum" ]] 2>/dev/null; then
    printf '  ok    %s (%s)\n' "$description" "$actual"
  else
    printf '  FAIL  %s (expected at least %s, got %s)\n' "$description" "$minimum" "$actual"
    failures=$((failures + 1))
  fi
}

echo "Restoring $DUMP into $SCRATCH"

psql "$ADMIN" -v ON_ERROR_STOP=1 -qtAc "CREATE DATABASE \"$SCRATCH\";" >/dev/null

# The scratch database on the same server as the admin connection.
TARGET="${ADMIN%/*}/$SCRATCH"

# --no-owner because the restore target's roles are its own business, and a dump
# insisting on the source's role names fails on a fresh database. Errors are not
# suppressed: pg_restore reporting problems is the first signal that the backup
# is not what it was assumed to be.
pg_restore --dbname="$TARGET" --no-owner --jobs=4 "$DUMP"

echo
echo "Checking the restored database"

# --- Every module schema came back ------------------------------------------
# A partial restore is the common failure, and it looks exactly like success
# until somebody uses the module that is missing.
# The schema is 'authz', not 'authorization' -- the latter is a reserved word in
# SQL and quoting it everywhere for ever was not worth the four saved characters.
# Eleven: ten module schemas and the kernel's. This said twelve, against a list
# naming eleven, so it could not pass -- and nothing noticed, because the script
# had never been run. A backup check that fails every good backup would have been
# discovered in the middle of the incident it exists for.
check "all ten module schemas plus the kernel restored" "11" "
  SELECT count(*) FROM information_schema.schemata
  WHERE schema_name IN (
    'kernel','identity','organization','authz','security','audit',
    'workflow','notifications','documents','integrations','configuration');"

# --- The audit trail is still append-only in shape --------------------------
# Partitioning is a property of the table, not of the rows. A restore that
# flattened it would work perfectly and quietly break retention, and nobody
# would find out for years.
check "the audit trail is still range-partitioned" "r" "
  SELECT partstrat FROM pg_partitioned_table
  JOIN pg_class ON pg_class.oid = pg_partitioned_table.partrelid
  JOIN pg_namespace ON pg_namespace.oid = pg_class.relnamespace
  WHERE pg_namespace.nspname = 'audit' AND pg_class.relname = 'audit_events';"

# The parent surviving without its children is a table that accepts nothing.
check_at_least "the audit partitions came back" 1 "
  SELECT count(*) FROM pg_inherits
  JOIN pg_class parent ON parent.oid = pg_inherits.inhparent
  JOIN pg_namespace ON pg_namespace.oid = parent.relnamespace
  WHERE pg_namespace.nspname = 'audit' AND parent.relname = 'audit_events';"

# --- Somebody can still get in ----------------------------------------------
# An empty identity schema restores perfectly and locks the company out for
# ever. This is the check that separates "the file restored" from "the company
# can come back".
check_at_least "at least one user account survived" 1 "
  SELECT count(*) FROM identity.users;"

check_at_least "at least one role survived" 1 "
  SELECT count(*) FROM authz.roles;"

check_at_least "at least one role assignment survived" 1 "
  SELECT count(*) FROM authz.user_role_assignments;"

# --- The schema and the binary can agree on where they are ------------------
# Without these the application tries to re-apply migrations onto a schema that
# already has them.
# One history table per context, and there are eleven: the Operations module
# owns no data and has none. This also said twelve.
check_at_least "every module's migration history is present" 11 "
  SELECT count(*) FROM information_schema.tables
  WHERE table_name = '__ef_migrations_history';"

echo
if [[ "$failures" -eq 0 ]]; then
  echo "The backup is usable."
  exit 0
fi

echo "$failures check(s) failed. This backup is NOT usable — treat it as an incident." >&2
exit 1
