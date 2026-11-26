#!/usr/bin/env bash
set -euo pipefail

# Smoke-test *generated* TypeScript packages the way an npm consumer sees them
# (spec 004 Amendment A1, FR-012 and FR-015).
#
# Builds and packs the sample-todo producer and its cross-package consumer
# sample-todo-service (plus metano-runtime), installs the three tarballs into
# a throwaway `"type": "module"` project outside the Bun workspace, then:
#   1. scans the packed JavaScript and declarations for relative specifiers
#      that do not name a `.js` file (FR-011);
#   2. imports both packages from plain Node ESM, with no resolve hook or
#      loader flag, and drives the service through the producer's types
#      (FR-012, FR-015), on every Node binary in SMOKE_NODE_BINARIES;
#   3. type-checks a consumer under NodeNext resolution with tsgo and
#      `skipLibCheck: false`, so the producers' own declarations are checked.
#
# Exits non-zero on the first failing check. Used by CI.
#
# Requires: bun, node 22+, a prior `bun install` at the repo root and in
# targets/js, and registry access (the service depends on hono).
#
# Optional:
#   SMOKE_NODE_BINARIES — space-separated Node executables to run step 2 with
#                         (default: `node`; the gate runs Node 22, 24 and 26).
#   RUNNER_TEMP         — parent directory for the throwaway projects.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
JS_DIR="$REPO_ROOT/targets/js"
TSGO="$REPO_ROOT/node_modules/.bin/tsgo"
readonly SMOKE_VERSION="0.0.0-smoke"
read -r -a NODE_BINARIES <<<"${SMOKE_NODE_BINARIES:-node}"

WORK_DIR="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/metano-generated-smoke.XXXXXX")"
STAGE_DIR="$WORK_DIR/stage"
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

assert_prerequisites() {
  [ -x "$TSGO" ] || fail "tsgo not found at $TSGO — run 'bun install' at the repo root first"

  for node_binary in "${NODE_BINARIES[@]}"; do
    command -v "$node_binary" >/dev/null || fail "$node_binary is not an executable Node"
  done
}

# Packs the package staged at $1 and prints the tarball path.
pack_staged() {
  (cd "$1" && bun pm pack --destination "$WORK_DIR" --quiet >/dev/null)
  find "$WORK_DIR" -maxdepth 1 -name "$(basename "$1")-*.tgz" -print -quit
}

# Copies the built package $1 (a folder under targets/js) into the stage and
# gives it a version plus `file:` tarball dependencies ($2: name=tarball pairs),
# since `workspace:*` means nothing outside the Bun workspace.
stage_package() {
  local name="$1"
  shift
  local stage="$STAGE_DIR/$name"

  mkdir -p "$stage"
  cp -R "$JS_DIR/$name/dist" "$JS_DIR/$name/src" "$JS_DIR/$name/package.json" "$stage/"

  node - "$stage/package.json" "$SMOKE_VERSION" "$@" <<'JS'
const fs = require("node:fs");
const [manifestPath, version, ...links] = process.argv.slice(2);
const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));

manifest.version = version;
delete manifest.devDependencies;

for (const link of links) {
  const [dependency, tarball] = link.split("=");
  manifest.dependencies[dependency] = `file:${tarball}`;
}

fs.writeFileSync(manifestPath, JSON.stringify(manifest, null, 2));
JS

  echo "$stage"
}

build_and_pack() {
  step "Building and packing metano-runtime, sample-todo and sample-todo-service"

  (cd "$JS_DIR/metano-runtime" && bun run build >/dev/null)
  (cd "$JS_DIR/sample-todo" && bun run build >/dev/null)
  (cd "$JS_DIR/sample-todo-service" && bun run build >/dev/null)

  (cd "$JS_DIR/metano-runtime" && bun pm pack --destination "$WORK_DIR" --quiet >/dev/null)
  RUNTIME_TARBALL="$(find "$WORK_DIR" -maxdepth 1 -name 'metano-runtime-*.tgz' -print -quit)"
  [ -n "$RUNTIME_TARBALL" ] || fail "bun pm pack produced no metano-runtime tarball"

  TODO_TARBALL="$(pack_staged "$(stage_package sample-todo "metano-runtime=$RUNTIME_TARBALL")")"
  [ -n "$TODO_TARBALL" ] || fail "bun pm pack produced no sample-todo tarball"

  SERVICE_TARBALL="$(pack_staged "$(stage_package sample-todo-service \
    "metano-runtime=$RUNTIME_TARBALL" "sample-todo=$TODO_TARBALL")")"
  [ -n "$SERVICE_TARBALL" ] || fail "bun pm pack produced no sample-todo-service tarball"

  echo "packed $(basename "$TODO_TARBALL") and $(basename "$SERVICE_TARBALL")"
}

write_consumer() {
  mkdir -p "$CONSUMER_DIR"

  cat >"$CONSUMER_DIR/package.json" <<'JSON'
{ "name": "metano-generated-smoke-consumer", "private": true, "type": "module" }
JSON

  cat >"$CONSUMER_DIR/tsconfig.json" <<'JSON'
{
  "compilerOptions": {
    "module": "nodenext",
    "moduleResolution": "nodenext",
    "target": "es2022",
    "lib": ["es2023", "esnext.disposable", "dom"],
    "types": [],
    "strict": true,
    "noEmit": true,
    "skipLibCheck": false
  },
  "include": ["entry-points.ts"]
}
JSON

  # Each `@ts-expect-error` proves the import resolved to real types: a
  # declaration that silently degraded to `any` would make the directive fail.
  cat >"$CONSUMER_DIR/entry-points.ts" <<'TS'
import { Priority, TodoItem } from "sample-todo/sample-todo";
import app from "sample-todo-service/dist/program.js";

export const item: TodoItem = new TodoItem("write the smoke test", false, Priority.High);

// @ts-expect-error — TodoItem is a class, not a number.
export const todoItemIsTyped: number = TodoItem;

// @ts-expect-error — the service's default export is a Hono app, not a number.
export const serviceIsTyped: number = app;
TS
}

install_consumer() {
  step "Installing the tarballs into a throwaway ESM project"

  write_consumer
  (cd "$CONSUMER_DIR" && bun add "$RUNTIME_TARBALL" "$TODO_TARBALL" "$SERVICE_TARBALL" >/dev/null)
}

# Prints every relative specifier in the installed generated packages that does
# not name a `.js` file (declarations included: generated code never names `.ts`).
find_non_js_relative_specifiers() {
  local quote="[\"']"
  local relative_specifier="(from|import)[[:space:]]*\(?[[:space:]]*${quote}\.{1,2}/[^\"']*${quote}"

  grep -rnoE "$relative_specifier" \
    "$CONSUMER_DIR/node_modules/sample-todo/dist" \
    "$CONSUMER_DIR/node_modules/sample-todo-service/dist" \
    --include='*.js' --include='*.d.ts' |
    grep -vE "\.js${quote}$" || true
}

assert_relative_js_specifiers() {
  step "Scanning the packed output for relative specifiers without .js"

  local offenders
  offenders="$(find_non_js_relative_specifiers)"

  [ -z "$offenders" ] || {
    echo "$offenders" >&2
    fail "generated relative specifiers must name a .js file"
  }

  echo "ok   every relative specifier names a .js file"
}

# Runs the cross-package scenario under the Node executable $1.
assert_node_runs() {
  step "Loading the packed packages from plain Node ESM ($("$1" --version))"

  (cd "$CONSUMER_DIR" && "$1" --input-type=module) <<'JS'
const { Priority, TodoItem } = await import("sample-todo/sample-todo");
const { default: app } = await import("sample-todo-service/dist/program.js");

const created = await app.fetch(
  new Request("http://smoke/todos", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ title: "smoke", priority: Priority.High }),
  }),
);

if (created.status !== 201) {
  throw new Error(`POST /todos answered ${created.status}`);
}

const listed = await (await app.fetch(new Request("http://smoke/todos"))).json();

if (listed.length !== 1 || listed[0].item.title !== "smoke") {
  throw new Error(`GET /todos answered ${JSON.stringify(listed)}`);
}

if (!(new TodoItem("a", false, Priority.Low) instanceof TodoItem)) {
  throw new Error("sample-todo/sample-todo does not export the TodoItem class");
}

console.log("ok   producer and cross-package consumer load and run");
JS
}

assert_nodenext_types() {
  step "Type-checking the consumer under NodeNext resolution (skipLibCheck: false)"

  (cd "$CONSUMER_DIR" && "$TSGO" -p .) || fail "tsgo type-check failed"
  echo "ok   tsgo ($("$TSGO" --version))"
}

main() {
  assert_prerequisites
  build_and_pack
  install_consumer
  assert_relative_js_specifiers

  for node_binary in "${NODE_BINARIES[@]}"; do
    assert_node_runs "$node_binary"
  done

  assert_nodenext_types
  step "generated-package smoke test passed"
}

main "$@"
