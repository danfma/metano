/**
 * A Node resolve hook for the generated output's extensionless relative
 * specifiers (`import { Estimate } from "./estimate"`): Bun and bundlers
 * resolve them, plain Node ESM does not. A relative specifier Node cannot
 * find is retried with the `.js` extension the build emits, so the Node run
 * loads the real built modules, in their own evaluation order. Loaded with
 * `node --import`. A consumer has no such hook: emitting Node-resolvable
 * specifiers is #573, which deletes this shim.
 */

import { registerHooks } from "node:module";

const isRelative = (specifier: string): boolean =>
  specifier.startsWith("./") || specifier.startsWith("../");

registerHooks({
  resolve(specifier, context, nextResolve) {
    try {
      return nextResolve(specifier, context);
    } catch (error) {
      if (!isRelative(specifier)) {
        throw error;
      }

      return nextResolve(`${specifier}.js`, context);
    }
  },
});
