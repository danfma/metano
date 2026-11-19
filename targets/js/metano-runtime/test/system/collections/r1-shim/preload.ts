import { existsSync } from "node:fs";
import { basename, dirname, join } from "node:path";
import { plugin } from "bun";

/**
 * TEMPORARY test-only stand-ins for the stage 011c modules that group R ships
 * in R1 (`default-comparer.ts`, `throw-helpers.ts`, `set-algebra.ts`,
 * `dictionary-views.ts`), written to their R0-frozen signatures so the 011h
 * sorted classes can be tested before R1 integrates. An import is redirected
 * here only while the real module is absent, so the shim is inert once R1
 * lands; it is deleted then (group H, after the rebase onto R1).
 */
const SHIMMED = new Set(["default-comparer.ts", "throw-helpers.ts", "set-algebra.ts", "dictionary-views.ts"]);

plugin({
  name: "metano-r1-shim",
  setup(build) {
    build.onResolve({ filter: /\/(default-comparer|throw-helpers|set-algebra|dictionary-views)\.ts$/ }, (args) => {
      const target = join(dirname(args.importer), args.path);

      if (!SHIMMED.has(basename(target)) || existsSync(target)) {
        return undefined;
      }

      return { path: join(import.meta.dir, basename(target)) };
    });
  },
});
