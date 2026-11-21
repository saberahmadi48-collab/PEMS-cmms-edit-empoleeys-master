#!/usr/bin/env bash
# Post-deploy smoke test of a deployed PEMS site (SCRUM-313 / #295), anonymous requests only: no login, no data written, no secret used.
# Usage: smoke-test.sh <site url without trailing slash> [label]
# Checks: /health answers Healthy, /health/database answers UpToDate, the login page renders the sign-in form, a stylesheet linked from the login
# page is served, an anonymous request to a protected page is sent to the login page (not served), an unknown path answers 404 (not 500).
# TLS verification is off (-k) like the health wait in deploy-site.yml; the certificate is reported by its own step.
set -uo pipefail

site="${1:?usage: smoke-test.sh <site url> [label]}"
site="${site%/}"
label="${2:-site}"
fail=0
tmp="$(mktemp -d)"

check() { # name, expected, actual
  if [ "$2" = "$3" ]; then echo "PASS  $1"; else echo "FAIL  $1 (expected $2, got $3)"; fail=1; fi
}
code_of() { curl -sS -k -o "$tmp/body" -w '%{http_code}' --max-time 60 "$@" 2> "$tmp/err" || echo 000; }

# the first request after a restart can be slow; give each check a few tries
retry_code() { # url
  local c i
  for i in 1 2 3 4 5; do
    c=$(code_of "$1")
    case "$c" in 000|502|503|504) sleep 10;; *) break;; esac
  done
  echo "$c"
}

echo "Smoke test of the ${label} site: ${site}"

check "GET /health is 200" 200 "$(retry_code "$site/health")"
body=$(tr -d '[:space:]' < "$tmp/body")
check "GET /health body is Healthy" Healthy "$body"

check "GET /health/database is 200" 200 "$(retry_code "$site/health/database")"
body=$(tr -d '[:space:]' < "$tmp/body")
check "GET /health/database body is UpToDate" UpToDate "$body"

check "GET /Account/Login is 200" 200 "$(retry_code "$site/Account/Login")"
if grep -qi '<form' "$tmp/body" && grep -qi 'type="password"' "$tmp/body"; then echo "PASS  login page has the sign-in form"; else echo "FAIL  login page has no password form"; fail=1; fi

css=$(grep -oiE 'href="[^"]+\.css[^"]*"' "$tmp/body" | head -1 | sed -E 's/^href="//; s/"$//; s/&amp;/\&/g')
if [ -n "$css" ]; then
  case "$css" in http*) url="$css";; /*) url="$site$css";; *) url="$site/$css";; esac
  check "stylesheet ${css%%\?*} is served" 200 "$(code_of "$url")"
else
  echo "FAIL  no stylesheet link found on the login page"; fail=1
fi

# a protected page must not be served to an anonymous visitor: 302 to the login page (or 401/403)
c=$(code_of --max-redirs 0 "$site/Admin/Users")
loc=$(curl -sS -k -o /dev/null -D - --max-redirs 0 --max-time 60 "$site/Admin/Users" 2>/dev/null | tr -d '\r' | grep -i '^location:' | head -1)
case "$c" in
  302) if echo "$loc" | grep -qi 'login'; then echo "PASS  /Admin/Users redirects an anonymous visitor to the login page"; else echo "FAIL  /Admin/Users redirected somewhere else"; fail=1; fi;;
  401|403) echo "PASS  /Admin/Users refuses an anonymous visitor ($c)";;
  *) echo "FAIL  /Admin/Users answered $c to an anonymous visitor"; fail=1;;
esac

check "unknown path is 404, not a server error" 404 "$(code_of "$site/pems-smoke-test-no-such-page")"

rm -rf "$tmp"
if [ "$fail" -ne 0 ]; then echo "::error::Smoke test of the ${label} site failed (see the FAIL lines above)."; exit 1; fi
echo "Smoke test of the ${label} site passed."
