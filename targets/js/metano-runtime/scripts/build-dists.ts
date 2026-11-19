/**
 * Writes the runtime's two published dists from tsgo's raw output in `build/` (spec 011
 * FR-058, W4-33): `dist/` with the production checks folded on and `dist-unchecked/`
 * with them folded off. Each module whose source reads the FR-058 guard
 * (`typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__`) is transformed by
 * esbuild with `__METANO_CHECKS__` defined `true` / `false` and `minifySyntax`, so the
 * checked dist reads no global at run time and the unchecked dist carries no check code,
 * whichever bundler (or none) consumes it. Every other file (declarations, maps, modules
 * without the guard) is copied unchanged into both. A consumer selects `dist-unchecked/`
 * with the `metano-unchecked` exports condition.
 *
 * Usage: bun scripts/build-dists.ts (run by `bun run build` after `tsgo -b .`)
 */
import { cpSync, mkdirSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from "node:fs";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { transformSync } from "esbuild";

const RUNTIME = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const RAW = join(RUNTIME, "build");
const DISTS = [
  { directory: join(RUNTIME, "dist"), checks: "true" },
  { directory: join(RUNTIME, "dist-unchecked"), checks: "false" },
] as const;
const GUARD = "__METANO_CHECKS__";

function filesUnder(root: string): string[] {
  return readdirSync(root).flatMap((name) => {
    const path = join(root, name);

    return statSync(path).isDirectory() ? filesUnder(path) : [path];
  });
}

/** The module with the guard folded to `checks`. */
function folded(source: string, checks: string): string {
  return transformSync(source, {
    format: "esm",
    minifySyntax: true,
    define: { [GUARD]: checks },
  }).code;
}

for (const { directory } of DISTS) {
  rmSync(directory, { recursive: true, force: true });
}

let foldedModules = 0;

for (const file of filesUnder(RAW)) {
  const path = relative(RAW, file);
  const guarded = file.endsWith(".js") && readFileSync(file, "utf8").includes(GUARD);

  for (const { directory, checks } of DISTS) {
    const target = join(directory, path);

    mkdirSync(dirname(target), { recursive: true });

    if (guarded) {
      writeFileSync(target, folded(readFileSync(file, "utf8"), checks));
    } else {
      cpSync(file, target);
    }
  }

  if (guarded) {
    foldedModules++;
  }
}

console.log(`dist/ and dist-unchecked/: ${foldedModules} modules folded`);
