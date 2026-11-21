#!/usr/bin/env bash
# Applies the EF migration chain to a real SQL Server, the same way the application does at start-up
# (SCRUM-413 / #383; docs/deployment/CODE_FIRST_DATABASE.md "Migration chain gate").
#
#   1) install: the whole chain on an empty database
#   2) upgrade: the chain up to the last migration of a base ref (the previous release, normally origin/main),
#      then the rest, so the migrations of this change run on a database that already has the old schema
#   3) the model and the migrations agree (no pending model changes)
# A failing migration is named in the output. Nothing here touches accounts or passwords.
#
# Needs: a Release build of PEMS.Web (dotnet build PEMS.Web -c Release), the dotnet-ef tool, a SQL Server login that may
# create databases. Environment: PEMS_CHAIN_SERVER_CONNECTION = connection string WITHOUT "Database=" (for example
# "Server=127.0.0.1,1433;User Id=sa;Password=...;TrustServerCertificate=True"). Argument 1 = base ref (default origin/main).
set -uo pipefail

base_ref="${1:-origin/main}"
server_cs="${PEMS_CHAIN_SERVER_CONNECTION:?set PEMS_CHAIN_SERVER_CONNECTION (connection string without Database=)}"
stamp="$(date +%s)"
ef=(dotnet ef --project PEMS.Migrations --startup-project PEMS.Web --no-build --configuration Release)

fail() { echo "::error::$*"; exit 1; }

# Runs "ef database update [target]" on one database; on failure prints the migration that was being applied.
update_to() {
  local db="$1" target="${2:-}" log
  log="$(mktemp)"
  if ConnectionStrings__PEMSConnection="${server_cs};Database=${db}" "${ef[@]}" database update ${target:+"$target"} >"$log" 2>&1; then
    tail -n 3 "$log"; rm -f "$log"; return 0
  fi
  cat "$log"
  local last; last="$(grep -o "Applying migration '[^']*'" "$log" | tail -n 1)"
  rm -f "$log"
  fail "migration chain failed on ${db}${target:+ (target ${target})}: ${last:-no migration was applied (connection or tooling problem)}"
}

# Fails when any migration of the chain is still pending on the database.
assert_nothing_pending() {
  local db="$1" out
  out="$(ConnectionStrings__PEMSConnection="${server_cs};Database=${db}" "${ef[@]}" migrations list 2>&1)" || { echo "$out"; fail "cannot list migrations on ${db}"; }
  if grep -q "(Pending)" <<<"$out"; then grep "(Pending)" <<<"$out"; fail "migrations are still pending on ${db} after the update"; fi
}

echo "== 1/3 install: whole chain on an empty database"
update_to "PEMS_Chain_Install_${stamp}"
assert_nothing_pending "PEMS_Chain_Install_${stamp}"

echo "== 2/3 upgrade: chain up to the last migration of ${base_ref}, then to head"
if ! git rev-parse --verify --quiet "${base_ref}^{commit}" >/dev/null; then
  # a force-pushed branch can lose its previous commit; the upgrade test then starts from origin/main instead of blocking the deploy
  [ "$base_ref" != "origin/main" ] || fail "base ref ${base_ref} not found (fetch it, or pass another ref)"
  echo "::warning::base ref ${base_ref} not found; using origin/main"
  base_ref="origin/main"
  git rev-parse --verify --quiet "${base_ref}^{commit}" >/dev/null || fail "base ref ${base_ref} not found (fetch it, or pass another ref)"
fi
base_last="$(git ls-tree -r --name-only "${base_ref}" -- PEMS.Migrations/Migrations/ \
  | grep -E '/[0-9]{14}_[A-Za-z0-9_]+\.cs$' | grep -v '\.Designer\.cs$' | sed -E 's#.*/([0-9]{14}_[A-Za-z0-9_]+)\.cs$#\1#' | sort | tail -n 1)"
[ -n "$base_last" ] || fail "no migration found in ${base_ref}"
echo "last migration of ${base_ref}: ${base_last}"
# A rollback deploy (older code than the base) has no such migration in its own chain: the upgrade test does not apply then.
if ! "${ef[@]}" migrations list --no-connect 2>/dev/null | grep -q "${base_last}"; then
  echo "::warning::${base_last} is not in this build's chain (rollback to older code?); upgrade test skipped, install test above still applies"
else
  update_to "PEMS_Chain_Upgrade_${stamp}" "$base_last"
  update_to "PEMS_Chain_Upgrade_${stamp}"
  assert_nothing_pending "PEMS_Chain_Upgrade_${stamp}"
fi

echo "== 3/3 model and migrations agree"
ConnectionStrings__PEMSConnection="${server_cs};Database=PEMS_Chain_Install_${stamp}" "${ef[@]}" migrations has-pending-model-changes \
  || fail "the entity model has changes without a migration (add one with dotnet ef migrations add)"

echo "Migration chain gate passed (base ${base_ref}, last base migration ${base_last})."
