// Runs the static probes of ONE transpiled module and prints one line per row, in the
// format of Runner.cs: `<Class>.<method> = <value>` or `<Class>.<method> THROWS <Type>: <message>`.
// Usage (Bun or Node): <runtime> run-probes.mjs <absolute path of the module> [<preload>...]
// The orchestrator (conformance.mjs) starts one process per module, under a timeout, so a
// load failure or a hang in one module cannot hide the rows of the others. Each preload
// module (tools/preloads.txt) is imported first, in order: a row that depends on how a
// consumer enters the package (through its barrel, for instance) names that entry there.
import { pathToFileURL } from "node:url";

const [modulePath, ...preloads] = process.argv.slice(2);

for (const preload of preloads) {
  await import(pathToFileURL(preload).href);
}

const module = await import(pathToFileURL(modulePath).href);
const lines = [];

for (const [className, cls] of Object.entries(module)) {
  if (typeof cls !== "function" || !className.endsWith("Probes")) {
    continue;
  }

  for (const name of Object.getOwnPropertyNames(cls)) {
    const row = cls[name];

    if (["length", "name", "prototype"].includes(name) || typeof row !== "function" || row.length !== 0) {
      continue;
    }

    let outcome;

    try {
      let value = row.call(cls);

      if (value instanceof Promise) {
        value = await value;
      }

      if (typeof value !== "string") {
        continue;
      }

      outcome = `= ${value}`;
    } catch (error) {
      outcome = `THROWS ${error?.constructor?.name ?? typeof error}: ${String(error?.message)}`;
    }

    lines.push(`${className}.${name} ${outcome.replaceAll("\r", "\\r").replaceAll("\n", "\\n")}`);
  }
}

console.log(lines.join("\n"));
