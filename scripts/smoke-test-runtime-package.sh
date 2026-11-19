#!/usr/bin/env bash
set -euo pipefail

# Smoke-test the *packed* metano-runtime the way a consumer sees it.
#
# Packs targets/js/metano-runtime, installs the tarball into a throwaway
# `"type": "module"` project outside the Bun workspace, then:
#   1. imports the public entry points (and file-level deep imports, such as
#      the per-class exception modules generated code imports) from plain
#      Node ESM, checks that an exception the root barrel throws is the class
#      a per-class module exports, and checks that a directory without an
#      explicit `exports` entry is not exported;
#   2. scans the packed JavaScript and declarations for extensionless
#      relative specifiers;
#   3. type-checks the entry points under NodeNext resolution with both the
#      native-preview compiler (tsgo) and stable TypeScript;
#   4. bundles a consumer entry from the installed package with bun and
#      esbuild (the latter also under the `source` condition, which resolves
#      the shipped .ts files) and runs each bundle under Node (spec 011 FR-011);
#   5. exercises both dists (spec 011 FR-058): a checked import, an unchecked
#      import through the `metano-unchecked` condition, and the bundler step
#      with the condition plus the `__METANO_CHECKS__` define.
#
# Exits non-zero on the first failing check. Used by CI and by the release
# workflow (before anything is tagged or published).
#
# Requires: bun, node 22+ (CI and release provision 24; verified on 22 and 24),
# a prior `bun install` at the repo root, and registry access (the consumer
# installs stable TypeScript and the runtime's own dependencies).
#
# Optional:
#   RUNNER_TEMP — parent directory for the throwaway consumer project.

readonly STABLE_TYPESCRIPT_VERSION="5.9.3"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
RUNTIME_DIR="$REPO_ROOT/targets/js/metano-runtime"
TSGO="$REPO_ROOT/node_modules/.bin/tsgo"
# esbuild is a dev dependency of the runtime package; bun installs it there or hoists it.
ESBUILD="$RUNTIME_DIR/node_modules/.bin/esbuild"
[ -x "$ESBUILD" ] || ESBUILD="$REPO_ROOT/node_modules/.bin/esbuild"

WORK_DIR="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/metano-runtime-smoke.XXXXXX")"
CONSUMER_DIR="$WORK_DIR/consumer"
PACKED_DIST="$CONSUMER_DIR/node_modules/metano-runtime/dist"
PACKED_UNCHECKED_DIST="$CONSUMER_DIR/node_modules/metano-runtime/dist-unchecked"
TARBALL="" # assigned by pack_runtime
trap 'rm -rf "$WORK_DIR"' EXIT

step() {
  echo
  echo "==> $*"
}

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

assert_prerequisites() {
  [ -x "$TSGO" ] || fail "tsgo not found at $TSGO — run 'bun install' at the repo root first"
  [ -x "$ESBUILD" ] || fail "esbuild not found at $ESBUILD — run 'bun install' at the repo root first"
  command -v node >/dev/null || fail "node is not on PATH"
}

pack_runtime() {
  step "Building and packing metano-runtime"

  (cd "$RUNTIME_DIR" && bun run build >/dev/null && bun pm pack --destination "$WORK_DIR" --quiet >/dev/null)

  TARBALL="$(find "$WORK_DIR" -maxdepth 1 -name 'metano-runtime-*.tgz' -print -quit)"
  [ -n "$TARBALL" ] || fail "bun pm pack produced no tarball"
  echo "packed $(basename "$TARBALL")"
}

write_consumer_manifest() {
  cat >"$CONSUMER_DIR/package.json" <<'JSON'
{ "name": "metano-runtime-smoke-consumer", "private": true, "type": "module" }
JSON
}

# Pinning `lib` below ESNext is what lets us keep `skipLibCheck: false`, so
# metano-runtime's own .d.ts files are checked: with ESNext, the built-in
# Temporal types clash with @js-temporal/polyfill's declarations (unrelated).
write_consumer_tsconfig() {
  cat >"$CONSUMER_DIR/tsconfig.json" <<'JSON'
{
  "compilerOptions": {
    "module": "nodenext",
    "moduleResolution": "nodenext",
    "target": "es2022",
    "lib": ["es2023"],
    "types": [],
    "strict": true,
    "noEmit": true,
    "skipLibCheck": false
  },
  "include": ["entry-points.ts"]
}
JSON
}

# Each `@ts-expect-error` proves that entry point resolved to real types: if a
# declaration silently degraded to `any`, the directive itself would error.
write_type_check_entry_points() {
  cat >"$CONSUMER_DIR/entry-points.ts" <<'TS'
import { isInt32 } from "metano-runtime";
import { SerializerContext } from "metano-runtime/system/json";
import { linq } from "metano-runtime/system/linq";
import { UUID } from "metano-runtime/system/uuid.js";
import { UUID as ExtensionlessUUID } from "metano-runtime/system/uuid";
import { InvalidOperationException } from "metano-runtime/system/exceptions/invalid-operation-exception";
import { Decimal } from "metano-runtime/system/decimal";
import type { ClassType } from "metano-runtime/system/reflection";

// @ts-expect-error — root: isInt32 returns boolean, not string.
export const rootIsTyped: string = isInt32(1);

// @ts-expect-error — system/json: SerializerContext is a class, not a number.
export const jsonIsTyped: number = SerializerContext;

// @ts-expect-error — system/linq: linq is a function, not a number.
export const linqIsTyped: number = linq;

// @ts-expect-error — deep import with .js: UUID is a namespace, not a number.
export const deepImportIsTyped: number = UUID;

// @ts-expect-error — extensionless deep import: same check.
export const extensionlessDeepImportIsTyped: number = ExtensionlessUUID;

// @ts-expect-error — per-class exception module, as generated code imports it.
export const exceptionModuleIsTyped: number = InvalidOperationException;

// @ts-expect-error — system/decimal: the configured Decimal is a class, not a number.
export const decimalIsTyped: number = Decimal;

// system/reflection: the exception classes' descriptor is a ClassType of the class.
export const runtimeTypeIsTyped: ClassType<InvalidOperationException> =
  InvalidOperationException.$runtimeType;

// @ts-expect-error — the descriptor is a ClassType, not a number.
export const runtimeTypeIsNotANumber: number = InvalidOperationException.$runtimeType;
TS
}

install_tarball_into_consumer() {
  (cd "$CONSUMER_DIR" && bun add "$TARBALL" >/dev/null && bun add -d "typescript@$STABLE_TYPESCRIPT_VERSION" >/dev/null)
  [ -d "$PACKED_DIST" ] || fail "installed package has no dist/ at $PACKED_DIST"
  [ -d "$PACKED_UNCHECKED_DIST" ] || fail "installed package has no dist-unchecked/ at $PACKED_UNCHECKED_DIST"
}

create_consumer() {
  step "Installing the tarball into a throwaway ESM project"

  mkdir -p "$CONSUMER_DIR"
  write_consumer_manifest
  write_consumer_tsconfig
  write_type_check_entry_points
  install_tarball_into_consumer
}

assert_node_imports() {
  step "Importing entry points from plain Node ESM ($(node --version))"

  (cd "$CONSUMER_DIR" && node --input-type=module) <<'JS'
const expectedExports = [
  ["metano-runtime", "HashCode"],
  ["metano-runtime/system/json", "SerializerContext"],
  ["metano-runtime/system/linq", "linq"],
  ["metano-runtime/system/uuid.js", "UUID"],
  ["metano-runtime/system/uuid", "UUID"],
  ["metano-runtime/system/exceptions/invalid-operation-exception", "InvalidOperationException"],
  ["metano-runtime/system/exceptions/divide-by-zero-exception", "DivideByZeroException"],
  ["metano-runtime/system/exceptions/divide-by-zero-exception.js", "DivideByZeroException"],
  ["metano-runtime/system/exceptions", "InvalidOperationException"],
  ["metano-runtime/system/exceptions/to-exception", "toException"],
  ["metano-runtime/system/collections", "HashSet"],
  ["metano-runtime/system/collections/hash-set", "HashSet"],
  ["metano-runtime/system/decimal", "Decimal"],
];

for (const [specifier, exportName] of expectedExports) {
  const loadedModule = await import(specifier);

  if (loadedModule[exportName] === undefined) {
    throw new Error(`${specifier} does not export ${exportName}`);
  }

  console.log(`ok   ${specifier} (${Object.keys(loadedModule).length} exports)`);
}

const { intDiv } = await import("metano-runtime");
const { DivideByZeroException } = await import("metano-runtime/system/exceptions/divide-by-zero-exception");

const thrown = (() => {
  try {
    intDiv(1, 0);
  } catch (error) {
    return error;
  }
})();

if (!(thrown instanceof DivideByZeroException) || !(thrown instanceof Error)) {
  throw new Error("the root barrel's intDiv does not throw the per-class DivideByZeroException");
}

console.log("ok   the root barrel throws the per-class exception classes");

// The configured decimal keeps System.Decimal's 29 digits (decimal.js alone keeps 20), and its
// decimal.js dependency installs with the runtime.
const { Decimal } = await import("metano-runtime/system/decimal");

if (new Decimal("79228162514264337593543950335").minus(1).toFixed() !== "79228162514264337593543950334") {
  throw new Error("metano-runtime/system/decimal does not keep System.Decimal's 29 digits");
}

console.log("ok   metano-runtime/system/decimal keeps 29 significant digits");

// The types-only reflection module resolves and loads (it has no runtime exports yet).
await import("metano-runtime/system/reflection");
console.log("ok   metano-runtime/system/reflection");
JS
}

assert_directory_subpath_rejected() {
  step "Checking that a directory without an exports entry is not exported"

  (cd "$CONSUMER_DIR" && node --input-type=module) <<'JS'
const directorySubpath = "metano-runtime/system";
const rejection = await import(directorySubpath).then(
  () => undefined,
  (error) => error,
);

if (rejection === undefined) {
  throw new Error(`${directorySubpath} unexpectedly resolved`);
}

if (rejection.code !== "ERR_MODULE_NOT_FOUND") {
  throw new Error(`${directorySubpath} failed with ${rejection.code}, expected ERR_MODULE_NOT_FOUND`);
}

console.log(`ok   ${directorySubpath} rejected (${rejection.code})`);
JS
}

# Prints every relative specifier in the packed files matching $1 whose target
# does not end in one of the extensions in $2 (an ERE alternation, e.g. "js|ts").
find_relative_specifiers_without_extension() {
  local file_glob="$1"
  local allowed_extensions="$2"
  local quote="[\"']"
  local relative_specifier="(from|import)[[:space:]]*\(?[[:space:]]*${quote}\.{1,2}/[^\"']*${quote}"

  grep -rnoE "$relative_specifier" "$PACKED_DIST" "$PACKED_UNCHECKED_DIST" --include="$file_glob" |
    grep -vE "\.(${allowed_extensions})${quote}$" || true
}

# Emitted JavaScript must name .js files. Declarations keep the source's .ts
# specifiers (tsgo's extension rewrite only touches JS emit) and TypeScript
# resolves those, so .d.ts files may name .js or .ts — never a bare path.
assert_no_extensionless_specifiers() {
  step "Scanning packed JavaScript and declarations for extensionless relative specifiers"

  local offenders
  offenders="$(find_relative_specifiers_without_extension '*.js' 'js')$(find_relative_specifiers_without_extension '*.d.ts' 'js|ts')"

  if [ -n "$offenders" ]; then
    echo "$offenders" >&2
    fail "relative specifiers must name a file with its extension"
  fi

  echo "ok   no extensionless relative specifiers"
}

assert_nodenext_types() {
  step "Type-checking entry points under NodeNext resolution"

  (cd "$CONSUMER_DIR" && "$TSGO" -p .) || fail "tsgo type-check failed"
  echo "ok   tsgo ($("$TSGO" --version))"

  (cd "$CONSUMER_DIR" && ./node_modules/.bin/tsc -p .) || fail "tsc type-check failed"
  echo "ok   tsc ($(cd "$CONSUMER_DIR" && ./node_modules/.bin/tsc --version))"
}

write_bundle_entry() {
  cat >"$CONSUMER_DIR/bundle-entry.js" <<'JS'
import { intDiv } from "metano-runtime";
import { InvalidOperationException, toException } from "metano-runtime/system/exceptions";

try {
  intDiv(1, 0);
} catch (caught) {
  console.log(toException(caught).toString());
}

console.log(new InvalidOperationException().message);
console.log(InvalidOperationException.$runtimeType.fullName);
JS
}

readonly EXPECTED_BUNDLE_OUTPUT="System.DivideByZeroException: Attempted to divide by zero.
Operation is not valid due to the current state of the object.
System.InvalidOperationException"

# Runs the bundle at $2 under Node and compares its output; $1 names the bundler.
assert_bundle_runs() {
  local output
  output="$(node "$2")" || fail "$1 bundle failed under Node"
  [ "$output" = "$EXPECTED_BUNDLE_OUTPUT" ] || fail "$1 bundle printed: $output"
  echo "ok   $1"
}

assert_bundlers() {
  step "Bundling a consumer entry from the installed package"

  write_bundle_entry

  local out="$CONSUMER_DIR/bundles"
  mkdir -p "$out"

  (cd "$CONSUMER_DIR" && bun build bundle-entry.js --target node --format esm --minify --outfile "$out/bun.js" >/dev/null) ||
    fail "bun build failed"
  assert_bundle_runs "bun build" "$out/bun.js"

  (cd "$CONSUMER_DIR" && "$ESBUILD" bundle-entry.js --bundle --minify --format=esm --platform=node --outfile="$out/esbuild.js" --log-level=warning) ||
    fail "esbuild failed"
  assert_bundle_runs "esbuild" "$out/esbuild.js"

  (cd "$CONSUMER_DIR" && "$ESBUILD" bundle-entry.js --bundle --minify --format=esm --platform=node --conditions=source --outfile="$out/esbuild-source.js" --log-level=warning) ||
    fail "esbuild (source condition) failed"
  assert_bundle_runs "esbuild (source condition)" "$out/esbuild-source.js"
}

write_checks_entry() {
  cat >"$CONSUMER_DIR/checks-entry.js" <<'JS'
import { List, throwModified } from "metano-runtime/system/collections";

const xs = List.of([1, 2, 3]);
let sum = 0;
{
  const $c = typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__;
  const $l = xs;
  const $a = $l._items;
  const $n = $a.length;
  const $v = $c ? $l._version : 0;
  for (let $i = 0; ; $i++) {
    if ($c && ($l._version !== $v || $a.length !== $n)) throwModified();
    if ($i === $n) break;
    sum += $a[$i];
  }
}
let outOfRange;
try {
  outOfRange = xs.get(99);
} catch (error) {
  outOfRange = error.name;
}
console.log(`${sum} ${outOfRange}`);
JS
}

# Runs `node` with the arguments after $2 and compares its output with $2; $1 names the case.
assert_checks_output() {
  local name="$1"
  local expected="$2"
  shift 2
  local output
  output="$(cd "$CONSUMER_DIR" && node "$@")" || fail "$name failed under Node"
  [ "$output" = "$expected" ] || fail "$name printed: $output (expected: $expected)"
  echo "ok   $name"
}

assert_both_dists() {
  step "Exercising the checked and the unchecked dist (spec 011 FR-058)"

  write_checks_entry

  local out="$CONSUMER_DIR/bundles"
  mkdir -p "$out"

  assert_checks_output "checked import (dist/)" "6 ArgumentOutOfRangeException" checks-entry.js
  assert_checks_output "unchecked import (--conditions=metano-unchecked)" "6 undefined" --conditions=metano-unchecked checks-entry.js

  (cd "$CONSUMER_DIR" && "$ESBUILD" checks-entry.js --bundle --minify --format=esm --platform=node --outfile="$out/checks-esbuild.js" --log-level=warning) ||
    fail "esbuild (checked) failed"
  assert_checks_output "esbuild, checked" "6 ArgumentOutOfRangeException" "$out/checks-esbuild.js"

  (cd "$CONSUMER_DIR" && "$ESBUILD" checks-entry.js --bundle --minify --format=esm --platform=node --conditions=metano-unchecked --define:__METANO_CHECKS__=false --outfile="$out/unchecked-esbuild.js" --log-level=warning) ||
    fail "esbuild (unchecked) failed"
  grep -q "\._version" "$out/unchecked-esbuild.js" && fail "the esbuild unchecked bundle still reads _version"
  assert_checks_output "esbuild, metano-unchecked + define" "6 undefined" "$out/unchecked-esbuild.js"

  (cd "$CONSUMER_DIR" && bun build checks-entry.js --target node --format esm --minify --conditions metano-unchecked --define __METANO_CHECKS__=false --outfile "$out/unchecked-bun.js" >/dev/null) ||
    fail "bun build (unchecked) failed"
  grep -q "\._version" "$out/unchecked-bun.js" && fail "the bun unchecked bundle still reads _version"
  assert_checks_output "bun build, metano-unchecked + define" "6 undefined" "$out/unchecked-bun.js"
}

main() {
  assert_prerequisites
  pack_runtime
  create_consumer
  assert_node_imports
  assert_directory_subpath_rejected
  assert_no_extensionless_specifiers
  assert_nodenext_types
  assert_bundlers
  assert_both_dists
  step "metano-runtime packaging smoke test passed"
}

main "$@"
