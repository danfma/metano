#!/usr/bin/env bash
set -euo pipefail

# Smoke-test the *packed* metano-runtime the way a consumer sees it.
#
# Packs targets/js/metano-runtime, installs the tarball into a throwaway
# `"type": "module"` project outside the Bun workspace, then:
#   1. imports the public entry points (and file-level deep imports) from
#      plain Node ESM, and checks directory subpaths are not exported;
#   2. scans the packed JavaScript and declarations for extensionless
#      relative specifiers;
#   3. type-checks the entry points under NodeNext resolution with both the
#      native-preview compiler (tsgo) and stable TypeScript.
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

WORK_DIR="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/metano-runtime-smoke.XXXXXX")"
CONSUMER_DIR="$WORK_DIR/consumer"
PACKED_DIST="$CONSUMER_DIR/node_modules/metano-runtime/dist"
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
TS
}

install_tarball_into_consumer() {
  (cd "$CONSUMER_DIR" && bun add "$TARBALL" >/dev/null && bun add -d "typescript@$STABLE_TYPESCRIPT_VERSION" >/dev/null)
  [ -d "$PACKED_DIST" ] || fail "installed package has no dist/ at $PACKED_DIST"
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
];

for (const [specifier, exportName] of expectedExports) {
  const loadedModule = await import(specifier);

  if (loadedModule[exportName] === undefined) {
    throw new Error(`${specifier} does not export ${exportName}`);
  }

  console.log(`ok   ${specifier} (${Object.keys(loadedModule).length} exports)`);
}
JS
}

assert_directory_subpath_rejected() {
  step "Checking that directory subpaths are not exported"

  (cd "$CONSUMER_DIR" && node --input-type=module) <<'JS'
const directorySubpath = "metano-runtime/system/collections";
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

  grep -rnoE "$relative_specifier" "$PACKED_DIST" --include="$file_glob" |
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

main() {
  assert_prerequisites
  pack_runtime
  create_consumer
  assert_node_imports
  assert_directory_subpath_rejected
  assert_no_extensionless_specifiers
  assert_nodenext_types
  step "metano-runtime packaging smoke test passed"
}

main "$@"
