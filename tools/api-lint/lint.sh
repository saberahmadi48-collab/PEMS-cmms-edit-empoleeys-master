#!/usr/bin/env bash
# API lint (SCRUM-436). Usage:
#   tools/api-lint/lint.sh            lint the approved OpenAPI snapshot; exit 1 on any error
#   tools/api-lint/lint.sh selftest   prove that every PEMS rule fires on tools/api-lint/samples-bad-api.json
# Needs Node 18+; Spectral is fetched by npx at the pinned version (Apache-2.0, a development tool only, never part of the product).
set -euo pipefail
cd "$(dirname "$0")/../.."
SPECTRAL="npx --yes @stoplight/spectral-cli@6.15.0"
RULESET=tools/api-lint/.spectral.yaml

if [ "${1:-}" = "selftest" ]; then
  out=$($SPECTRAL lint tools/api-lint/samples-bad-api.json --ruleset "$RULESET" --format json || true)
  missing=0
  for rule in pems-path-form pems-no-get-actions pems-401-403 pems-error-bodies-are-problem-json pems-write-needs-if-match pems-collection-post-is-201 pems-json-names-are-camel-case pems-security-declared pems-operation-documented; do
    if ! grep -q "\"code\": *\"$rule\"" <<<"$out"; then echo "SELFTEST FAILED: rule $rule did not fire on the bad sample"; missing=1; fi
  done
  [ "$missing" = 0 ] && echo "selftest ok: every PEMS rule fires on the bad sample"
  exit "$missing"
fi

$SPECTRAL lint PEMS.Tests/Api/Snapshots/openapi-v1.json --ruleset "$RULESET" --fail-severity warn
