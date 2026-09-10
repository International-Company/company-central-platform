#!/usr/bin/env bash
#
# Puts a handful of rows into a freshly started Platform, so the end-to-end
# sweeps look at populated screens.
#
# ---------------------------------------------------------------------------
# WHY THIS EXISTS
#
# An empty table passes almost everything. The accessibility sweep ran green on
# WCAG 2.2 against a Platform with no data in it, and failed the moment one
# screen had four real rows -- on target-size, which cannot fire when there are
# no row actions to measure. Every other assertion in that suite was asking the
# same easier question: a table renders its empty state correctly, no sort
# control has anything to sort, and no row action exists to be too small, badly
# labelled or wrongly ordered.
#
# So this is not a convenience. It is the difference between a suite that checks
# the product and one that checks its empty state.
# ---------------------------------------------------------------------------
#
# Everything created is obviously fake and lives only in the CI database, which
# is thrown away with the job. No real name, address or number appears here.
#
# Usage:
#   scripts/seed-e2e-data.sh
#
#   BASE_URL       default http://localhost:5080
#   E2E_USERNAME   the bootstrap administrator
#   E2E_PASSWORD   its password
set -euo pipefail

BASE="${BASE_URL:-http://localhost:5080}"
USERNAME="${E2E_USERNAME:?Set E2E_USERNAME}"
PASSWORD="${E2E_PASSWORD:?Set E2E_PASSWORD}"

# ---------------------------------------------------------------------------
# Sign in
# ---------------------------------------------------------------------------
token="$(
  curl -sf -X POST "$BASE/api/v1/auth/login" \
    -H 'Content-Type: application/json' \
    -d "{\"username\":\"$USERNAME\",\"password\":\"$PASSWORD\"}" \
  | python3 -c 'import json,sys; print(json.load(sys.stdin)["accessToken"])'
)"

if [[ -z "$token" ]]; then
  echo "Could not sign in as $USERNAME. Nothing can be seeded." >&2
  exit 1
fi

auth=(-H "Authorization: Bearer $token" -H 'Content-Type: application/json')

# ---------------------------------------------------------------------------
# A tolerant POST.
#
# Seeding runs against a Platform that may already hold some of this -- a
# re-run, or a company created by an earlier step. A conflict is the seed having
# already happened, which is success. Only an unexpected status is a failure.
# ---------------------------------------------------------------------------
post() {
  local path="$1" body="$2" response status

  response="$(curl -s -o /tmp/seed-body -w '%{http_code}' -X POST "$BASE$path" "${auth[@]}" -d "$body")"
  status="$response"

  case "$status" in
    2*) cat /tmp/seed-body ;;
    409|422) printf '' ;;   # already there, or refused as a duplicate
    *)
      echo "POST $path answered $status: $(cat /tmp/seed-body)" >&2
      return 1
      ;;
  esac
}

id_of() {
  python3 -c 'import json,sys
raw = sys.stdin.read().strip()
print(json.loads(raw)["id"] if raw else "")'
}

echo "Seeding $BASE"

# ---------------------------------------------------------------------------
# The company. Every read in Organization resolves it first, so nothing else
# can be created until it exists.
# ---------------------------------------------------------------------------
post /api/v1/organization/company \
  '{"code":"E2E","nameAr":"شركة الاختبار","nameEn":"End To End Company","defaultLocale":"ar"}' \
  > /dev/null || true

company_present="$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/organization/company" "${auth[@]}")"

if [[ "$company_present" != "200" ]]; then
  echo "No company after seeding; the rest cannot be created." >&2
  exit 1
fi

# ---------------------------------------------------------------------------
# A small hierarchy: a division with two departments under it. Enough for the
# tree to have depth, which is what the organization screen actually renders.
# ---------------------------------------------------------------------------
division="$(post /api/v1/organization/units \
  '{"parentId":null,"unitType":"Division","code":"OPS","nameAr":"العمليات","nameEn":"Operations"}' | id_of)"

if [[ -n "$division" ]]; then
  finance="$(post /api/v1/organization/units \
    "{\"parentId\":\"$division\",\"unitType\":\"Department\",\"code\":\"FIN\",\"nameAr\":\"المالية\",\"nameEn\":\"Finance\"}" | id_of)"

  post /api/v1/organization/units \
    "{\"parentId\":\"$division\",\"unitType\":\"Department\",\"code\":\"HR\",\"nameAr\":\"الموارد البشرية\",\"nameEn\":\"People\"}" \
    > /dev/null || true
else
  finance=""
fi

# ---------------------------------------------------------------------------
# Employees, so the employee table has rows with a unit to filter by.
# ---------------------------------------------------------------------------
if [[ -n "$finance" ]]; then
  for i in 1 2 3; do
    post /api/v1/organization/employees \
      "{\"employeeNumber\":\"E00$i\",\"fullNameAr\":\"موظف تجريبي $i\",\"fullNameEn\":\"Test Employee $i\",\"unitId\":\"$finance\"}" \
      > /dev/null || true
  done
fi

# ---------------------------------------------------------------------------
# Users and roles. Two of each: one row proves a table renders, and two prove
# it renders a list -- sorting, striping and row actions all need a second row
# to be wrong in an interesting way.
# ---------------------------------------------------------------------------
for i in 1 2; do
  post /api/v1/users \
    "{\"username\":\"seeded$i\",\"email\":\"seeded$i@example.invalid\",\"displayName\":\"Seeded User $i\",\"initialPassword\":\"seeded-not-a-real-secret-2026\"}" \
    > /dev/null || true
done

post /api/v1/roles \
  '{"code":"e2e-reader","nameAr":"قارئ","nameEn":"Reader","description":"Created by the end-to-end seed."}' \
  > /dev/null || true

post /api/v1/roles \
  '{"code":"e2e-editor","nameAr":"محرر","nameEn":"Editor","description":"Created by the end-to-end seed."}' \
  > /dev/null || true

echo "Seeded."
