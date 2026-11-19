#!/usr/bin/env bash
set -euo pipefail

# Exercise the retry policy without depending on a real nuget.org outage.
source "$(dirname "${BASH_SOURCE[0]}")/smoke-test-build-package.sh"

python3 "$SCRIPT_DIR/test-build-package-process.py" "$WORK_DIR"

attempts=0
waits=""
sleep() { waits="$waits $1"; }

run_dotnet() {
  attempts=$((attempts + 1))
  if [ "$attempts" -lt 3 ]; then
    echo 'error NU1301: Unable to load the service index for nuget.org.' >&2
    return 72
  fi
}

restore_with_retry restore fixture.csproj || fail "a transient download failure did not recover"
[ "$attempts" = 3 ] || fail "expected three download attempts"
[ "$waits" = ' 2 4' ] || fail "retry backoff was not bounded"

attempts=0
run_dotnet() {
  attempts=$((attempts + 1))
  echo 'error NU1301: nuget.org is unavailable.' >&2
  return 72
}

if restore_with_retry restore fixture.csproj; then
  fail "persistent download failures were ignored"
else
  [ "$?" = 72 ] || fail "retry did not preserve the failed command exit code"
fi
[ "$attempts" = 3 ] || fail "persistent failures exceeded the retry bound"

attempts=0
run_dotnet() {
  attempts=$((attempts + 1))
  echo 'error NU1101: Unable to find package Missing.Package.' >&2
  return 73
}

if restore_with_retry restore fixture.csproj; then
  fail "a missing package was ignored"
else
  [ "$?" = 73 ] || fail "the permanent failure exit code changed"
fi
[ "$attempts" = 1 ] || fail "a permanent package error was retried"

# Bare status-like numbers in diagnostics, versions and paths are not HTTP failures.
for message in \
  'error CS1503: Argument 1 cannot convert fixture.cs:429.' \
  'error NU1101: Unable to find package Library.502.' \
  'error: /packages/503/file.cs failed at line 504.' \
  'error: HTTP 5020 is not a retryable status.' \
  'error: HTTP 4291 is not a retryable status.' \
  'error: HTTP 500 (Internal Server Error).' \
  'error: HTTP log entry for file 503.cs.' \
  'error: status code 404 (Not Found).'; do
  attempts=0
  run_dotnet() {
    attempts=$((attempts + 1))
    echo "$message" >&2
    return 73
  }
  if restore_with_retry restore fixture.csproj; then
    fail "a permanent error was ignored: $message"
  else
    [ "$?" = 73 ] || fail "the permanent error exit code changed"
  fi
  [ "$attempts" = 1 ] || fail "a non-HTTP number was retried: $message"
done

for message in \
  'Response status code does not indicate success: 429 (Too Many Requests).' \
  'error: status code: 502 (Bad Gateway).' \
  'error: HTTP 503 (Service Unavailable).' \
  'error: http: 504 (Gateway Timeout).' \
  'error: status code 429 (Too Many Requests).'; do
  attempts=0
  waits=""
  run_dotnet() {
    attempts=$((attempts + 1))
    if [ "$attempts" -lt 2 ]; then
      echo "$message" >&2
      return 72
    fi
  }
  restore_with_retry restore fixture.csproj || fail "HTTP retry did not recover: $message"
  [ "$attempts" = 2 ] || fail "a transient HTTP error was not retried: $message"
  [ "$waits" = ' 2' ] || fail "HTTP retry backoff changed"
done

[[ "$NUGET_PACKAGES" == "$WORK_DIR/"* ]] || fail "the package cache is not isolated"
[[ "$NUGET_HTTP_CACHE_PATH" != "$WORK_DIR/"* ]] || fail "the download cache will be deleted after the smoke run"
echo "Build-package smoke retry/cache policy tests passed."
