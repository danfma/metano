// Node resolve hook for the generated output's extensionless relative specifiers
// (`import { Foo } from "./foo"`): Bun and bundlers resolve them, plain Node ESM does not.
// Retries a relative specifier Node cannot find with the `.js` the build emits, then with
// `/index.js`. Same shim as targets/js/sample-issue-tracker/test/node/extensionless-imports.ts,
// to be dropped together with it (#573). Loaded with `node --import`.
import { registerHooks } from "node:module";

const isRelative = (specifier) => specifier.startsWith("./") || specifier.startsWith("../");

registerHooks({
  resolve(specifier, context, nextResolve) {
    try {
      return nextResolve(specifier, context);
    } catch (error) {
      if (!isRelative(specifier)) {
        throw error;
      }

      try {
        return nextResolve(`${specifier}.js`, context);
      } catch {
        return nextResolve(`${specifier}/index.js`, context);
      }
    }
  },
});
