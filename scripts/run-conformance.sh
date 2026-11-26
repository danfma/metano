#!/usr/bin/env bash
# Runs the transpile-conformance set (tests/Conformance, see its README.md): builds the
# TypeScript CLI and metano-runtime, transpiles the probes, compiles them for Node, and
# compares the Bun and Node output of every row with the recorded .NET output and with the
# recorded known gaps. Exit code 1 when a row CHANGED (or has no recorded .NET output).
#
#   scripts/run-conformance.sh                  # normal run (against the recorded .NET output)
#   scripts/run-conformance.sh --record         # re-record .NET, Bun and Node output
#   scripts/run-conformance.sh --record-dotnet  # re-record only the .NET output
#   scripts/run-conformance.sh --live-dotnet    # compare against a live `dotnet run` of the probes
#   scripts/run-conformance.sh --all            # list every row, not only the ones that are not MATCH
#   scripts/run-conformance.sh --filter REGEX   # only the rows whose name matches
#   scripts/run-conformance.sh --no-build       # reuse the last transpile (.work/) and the built CLI/runtime
#
# The disposable rows also run downlevelled (tools/downlevel-check.mjs, spec FR-111).
# Every probe process runs under a timeout (CONFORMANCE_TIMEOUT_MS, default 30000).
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
conformance="$root/tests/Conformance"
work="$conformance/.work"
cli="$root/src/Metano.Compiler.TypeScript/bin/Debug/net10.0/Metano.Compiler.TypeScript.dll"
tsgo="$root/node_modules/.bin/tsgo"

build=1
forward=()
for arg in "$@"; do
  if [ "$arg" = "--no-build" ]; then
    build=0
  else
    forward+=("$arg")
  fi
done

step() { echo "== $*" >&2; }

if [ "$build" = 1 ]; then
  step "build the TypeScript CLI and metano-runtime"
  dotnet build "$root/src/Metano.Compiler.TypeScript" -c Debug -v q -nologo -clp:ErrorsOnly >&2
  (cd "$root/targets/js/metano-runtime" && bun run build >&2)

  step "transpile the probes"
  rm -rf "$work"
  for package in lib checked-enums probes; do
    mkdir -p "$work/$package"
    cp "$conformance/tools/workspace/$package/package.json" "$conformance/tools/workspace/$package/tsconfig.json" "$work/$package/"
  done
  # The probes are sloppy on purpose, so the CLI warns a lot: the log is kept, not printed.
  dotnet "$cli" -p "$conformance/xpkg/Lib/ConformanceLib.csproj" -o "$work/lib/src" --clean >"$work/lib/transpile.log" 2>&1 \
    || { tail -20 "$work/lib/transpile.log" >&2; exit 1; }
  dotnet "$cli" -p "$conformance/xpkg/CheckedEnums/CheckedEnums.csproj" -o "$work/checked-enums/src" --clean >"$work/checked-enums/transpile.log" 2>&1 \
    || { tail -20 "$work/checked-enums/transpile.log" >&2; exit 1; }
  dotnet "$cli" -p "$conformance/Conformance.csproj" -o "$work/probes/src" --clean >"$work/probes/transpile.log" 2>&1 \
    || { tail -20 "$work/probes/transpile.log" >&2; exit 1; }
  # The CLI merges its own imports/exports into package.json; the fixed one resolves for both runtimes.
  for package in lib checked-enums probes; do
    cp "$conformance/tools/workspace/$package/package.json" "$work/$package/"
  done
  mkdir -p "$work/probes/node_modules" "$work/lib/node_modules" "$work/checked-enums/node_modules"
  for dependency in "$work/lib/node_modules" "$work/checked-enums/node_modules" "$work/probes/node_modules"; do
    ln -sfn "$root/targets/js/metano-runtime" "$dependency/metano-runtime"
    ln -sfn "$root/targets/js/metano-runtime/node_modules/decimal.js" "$dependency/decimal.js"
    ln -sfn "$root/targets/js/metano-runtime/node_modules/@js-temporal" "$dependency/@js-temporal"
  done
  ln -sfn "$work/lib" "$work/probes/node_modules/conformance-lib"
  ln -sfn "$work/checked-enums" "$work/probes/node_modules/conformance-checked-enums"

  step "compile the probes for Node (tsgo)"
  for package in lib checked-enums probes; do
    # Type errors in the generated code are informational here: the emit still happens.
    (cd "$work/$package" && "$tsgo" -p . >"$work/$package/tsgo.log" 2>&1 || true)
    echo "tsgo $package: $(grep -c 'error TS' "$work/$package/tsgo.log" || true) type error(s) (see $work/$package/tsgo.log)" >&2
  done

  step "compile the probes downlevelled (target es2022, importHelpers)"
  ln -sfn "$root/node_modules/tslib" "$work/probes/node_modules/tslib"
  cp "$conformance/tools/workspace/probes/tsconfig.downlevel.json" "$work/probes/"
  (cd "$work/probes" && "$tsgo" -p tsconfig.downlevel.json >"$work/probes/tsgo-downlevel.log" 2>&1 || true)
fi

step "run the rows under Bun and Node"
status=0
bun "$conformance/tools/conformance.mjs" ${forward[@]+"${forward[@]}"} || status=$?

step "run the downlevelled using rows (Bun, Node, and without Symbol.dispose)"
bun "$conformance/tools/downlevel-check.mjs" || status=1

exit "$status"
