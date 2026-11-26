// The downlevelled leg of the conformance set for C# `using` (spec FR-111): the disposable
// probe modules compiled at target es2022 with `importHelpers` (TypeScript's
// __addDisposableResource / __disposeResources from tslib) run under Bun and Node, both as
// is and on a simulated engine without `Symbol.dispose` / `SuppressedError`
// (without-symbol-dispose.mjs), where the metano-runtime polyfill must install the symbol
// before the generated classes are defined. Every row must print what .NET prints.
// Run by scripts/run-conformance.sh after the probes are compiled (.work/probes/dist-downlevel),
// with its --filter; it is skipped when no disposable row matches, and fails when the
// downlevelled build is missing (a --no-build run before any full run).
import { spawnSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const toolsDir = dirname(fileURLToPath(import.meta.url));
const conformance = join(toolsDir, "..");
const dist = join(conformance, ".work", "probes", "dist-downlevel", "conformance");
const timeoutMs = Number(process.env.CONFORMANCE_TIMEOUT_MS ?? 30000);
const preload = join(toolsDir, "without-symbol-dispose.mjs");

const modules = [
  "native-using-probes",
  "disposable-probes",
  "disposable-null-guard-probes",
  "disposable-overload-probes",
];

const classes = ["NativeUsingProbes.", "DisposableProbes.", "DisposableNullGuardProbes.", "DisposableOverloadProbes."];

const filter = filterOf(process.argv.slice(2));

const expected = readFileSync(join(conformance, "recorded", "dotnet.txt"), "utf8")
  .split("\n")
  .filter((line) => classes.some((name) => line.startsWith(name)))
  .filter((line) => filter.test(rowName(line)))
  .sort();

if (expected.length === 0) {
  console.error("downlevel: skipped, no disposable row matches the filter");
  process.exit(0);
}

if (!existsSync(dist)) {
  console.error(
    `downlevel: FAILED, ${dist} is missing; run scripts/run-conformance.sh without --no-build first`,
  );
  process.exit(1);
}

const variants = [
  ["bun", []],
  ["node", []],
  ["bun", [preload]],
  ["node", [preload]],
];

let failed = false;

for (const [runtime, preloads] of variants) {
  const actual = modules
    .flatMap((module) => run(runtime, module, preloads))
    .filter((line) => line.includes(" FAILED (") || filter.test(rowName(line)))
    .sort();
  const label = `${runtime}${preloads.length > 0 ? " (no Symbol.dispose)" : ""}`;
  const missing = expected.filter((line) => !actual.includes(line));

  if (missing.length > 0 || actual.length !== expected.length) {
    failed = true;
    console.error(`downlevel ${label}: ${missing.length} row(s) differ from .NET`);

    for (const line of missing) {
      console.error(`  expected ${line}`);
    }

    for (const line of actual.filter((row) => !expected.includes(row))) {
      console.error(`  actual   ${line}`);
    }
  } else {
    console.error(`downlevel ${label}: ${actual.length} rows MATCH`);
  }
}

process.exit(failed ? 1 : 0);

/** The `--filter REGEX` of run-conformance.sh (every row when absent). */
function filterOf(args) {
  const index = args.indexOf("--filter");

  return index >= 0 && args[index + 1] !== undefined ? new RegExp(args[index + 1]) : /./;
}

/** `Class.method` of a printed row. */
function rowName(line) {
  return line.split(" ")[0];
}

function run(runtime, module, preloads) {
  const result = spawnSync(
    runtime,
    [join(toolsDir, "run-probes.mjs"), join(dist, `${module}.js`), ...preloads],
    { encoding: "utf8", timeout: timeoutMs },
  );

  if (result.status !== 0) {
    return [`${module} FAILED (${result.status}): ${result.stderr.trim()}`];
  }

  return result.stdout.split("\n").filter((line) => line.length > 0);
}
