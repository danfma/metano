// Node ESM check of spec 011 FR-029: import the built base module first, unbundled,
// then its guard module, and call the strict guard on plain objects. The generated
// relative specifiers are extensionless (the sample targets bundlers), so a resolve
// hook adds the `.js` the build writes.
import { registerHooks } from "node:module";

registerHooks({
  resolve(specifier, context, nextResolve) {
    if (/^\.\.?\//.test(specifier) && !/\.[cm]?js$/.test(specifier)) {
      return nextResolve(`${specifier}.js`, context);
    }

    return nextResolve(specifier, context);
  },
});

const dist = new URL("../../dist/sample-runtime-types/unions/", import.meta.url);

await import(new URL("token.js", dist).href);

const { isToken } = await import(new URL("token.guard.js", dist).href);

console.log(isToken({ kind: "TextToken", text: "a" }), isToken({ kind: "TextToken", text: 1 }));
