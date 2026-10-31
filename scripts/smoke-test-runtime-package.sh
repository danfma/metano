#!/usr/bin/env bash
set -euo pipefail

# Smoke-test the *packed* metano-runtime the way a consumer sees it.
#
# Packs targets/js/metano-runtime, installs the tarball into a throwaway
# `"type": "module"` project outside the Bun workspace, then:
#   1. imports the public entry points from plain Node ESM;
#   2. checks that file-level deep imports load and directory deep imports
#      are not resolved as (unsupported) directory imports;
#   3. scans the packed JavaScript for extensionless relative specifiers;
#   4. type-checks the entry points under NodeNext resolution with both the
#      native-preview compiler (tsgo) and stable TypeScript.
#
# Exits non-zero on the first failing check. Used by CI and by the release
# workflow (before anything is tagged or published).
#
# Requires: bun, node (>= 20), and a prior `bun install` at the repo root.
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
trap 'rm -rf "$WORK_DIR"' EXIT

step() {
  echo
  echo "==> $*"
}

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

pack_runtime() {
  step "Building and packing metano-runtime"
  (cd "$RUNTIME_DIR" && bun run build >/dev/null && bun pm pack --destination "$WORK_DIR" --quiet >/dev/null)

  TARBALL="$(find "$WORK_DIR" -maxdepth 1 -name 'metano-runtime-*.tgz' -print -quit)"
  [ -n "$TARBALL" ] || fail "bun pm pack produced no tarball"
  echo "packed $(basename "$TARBALL")"
}

create_consumer() {
  step "Installing the tarball into a throwaway ESM project"
  mkdir -p "$CONSUMER_DIR"

  cat >"$CONSUMER_DIR/package.json" <<'JSON'
{ "name": "metano-runtime-smoke-consumer", "private": true, "type": "module" }
JSON

  # lib is pinned below ESNext on purpose: @js-temporal/polyfill's own
  # declarations clash with the built-in ESNext Temporal types when library
  # checking is on. Pinning keeps skipLibCheck off for metano-runtime itself.
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

  cat >"$CONSUMER_DIR/entry-points.ts" <<'TS'
import { HashCode, isInt32 } from "metano-runtime";
import { SerializerContext } from "metano-runtime/system/json";
import { linq } from "metano-runtime/system/linq";
import type { UUID } from "metano-runtime/system/uuid.js";
import type { UUID as ExtensionlessUUID } from "metano-runtime/system/uuid";

const isInteger: boolean = isInt32(1);

// @ts-expect-error — proves declarations resolved to real types, not `any`.
const notAString: string = isInt32(1);

export const entryPoints: readonly unknown[] = [HashCode, SerializerContext, linq, isInteger, notAString];
export type DeepImports = [UUID, ExtensionlessUUID];
TS

  (cd "$CONSUMER_DIR" && bun add "$TARBALL" >/dev/null && bun add -d "typescript@$STABLE_TYPESCRIPT_VERSION" >/dev/null)
}

assert_node_imports() {
  step "Importing entry points from plain Node ESM ($(node --version))"

  (cd "$CONSUMER_DIR" && node --input-type=module) <<'JS'
const expectations = [
  ["metano-runtime", "HashCode"],
  ["metano-runtime/system/json", "SerializerContext"],
  ["metano-runtime/system/linq", "linq"],
  ["metano-runtime/system/uuid.js", "UUID"],
  ["metano-runtime/system/uuid", "UUID"],
];

for (const [specifier, exportName] of expectations) {
  const module = await import(specifier);

  if (module[exportName] === undefined) {
    throw new Error(`${specifier} does not export ${exportName}`);
  }

  console.log(`ok   ${specifier} (${Object.keys(module).length} exports)`);
}

try {
  await import("metano-runtime/system/collections");
  throw new Error("metano-runtime/system/collections unexpectedly resolved");
} catch (error) {
  if (error.code === "ERR_UNSUPPORTED_DIR_IMPORT") {
    throw new Error("the export map still advertises directory subpaths");
  }

  console.log(`ok   metano-runtime/system/collections rejected (${error.code})`);
}
JS
}

assert_no_extensionless_specifiers() {
  step "Scanning packed JavaScript for extensionless relative specifiers"
  local packed_dist="$CONSUMER_DIR/node_modules/metano-runtime/dist"
  local relative_specifier='(from|import)[[:space:]]*\(?[[:space:]]*["'"'"']\.{1,2}/[^"'"'"']*["'"'"']'
  local offenders

  offenders="$(grep -rnoE "$relative_specifier" "$packed_dist" --include='*.js' | grep -vE "\.js[\"']$" || true)"

  if [ -n "$offenders" ]; then
    echo "$offenders" >&2
    fail "relative specifiers must name a .js file"
  fi

  echo "ok   no extensionless relative specifiers"
}

assert_nodenext_types() {
  step "Type-checking entry points under NodeNext resolution"
  (cd "$CONSUMER_DIR" && "$TSGO" -p .) || fail "tsgo type-check failed"
  echo "ok   tsgo $("$TSGO" --version | awk '{print $2}')"

  (cd "$CONSUMER_DIR" && ./node_modules/.bin/tsc -p .) || fail "tsc type-check failed"
  echo "ok   tsc $STABLE_TYPESCRIPT_VERSION"
}

[ -x "$TSGO" ] || fail "tsgo not found at $TSGO — run 'bun install' at the repo root first"

pack_runtime
create_consumer
assert_node_imports
assert_no_extensionless_specifiers
assert_nodenext_types

step "metano-runtime packaging smoke test passed"
