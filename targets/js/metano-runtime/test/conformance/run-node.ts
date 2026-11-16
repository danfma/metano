/**
 * Runs every conformance file under Node: `node test/conformance/run-node.ts` from
 * `targets/js/metano-runtime`. Bun's JavaScriptCore hides module-load-order and number
 * formatting differences V8 shows, so the rows run on both.
 */

import { checkRows } from "./conformance-rows.ts";
import {
  parsingRegistry,
  stringRegistry,
  temporalRegistry,
} from "./registries.ts";

const files = {
  "strings.json": stringRegistry,
  "temporal.json": temporalRegistry,
  "parsing.json": parsingRegistry,
} as const;

const mismatches = Object.entries(files).flatMap(([file, registry]) =>
  checkRows(file, registry).map((mismatch) => `${file}: ${mismatch}`),
);

if (mismatches.length > 0) {
  console.error(mismatches.join("\n"));
  process.exit(1);
}

console.log(`conformance rows agree with .NET (${Object.keys(files).join(", ")})`);
