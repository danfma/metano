/**
 * The production-checks constant of spec 011 FR-058. A bundler's `define`
 * (`__METANO_CHECKS__: "false"`) turns the bounds checks and the version
 * tracking of the runtime collections into dead code; when the identifier is
 * not defined, every check runs. Every guarded site reads it inline as
 * `(typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__)`: a
 * module-scope constant derived from it would read a global at module load,
 * a side effect for bundlers (NFR-006).
 *
 * `MetanoChecks` exists so that the modules which re-export it carry this
 * declaration into every entry point's `.d.ts`, where the code generated for a
 * dependent package also needs it.
 */
declare global {
  const __METANO_CHECKS__: boolean | undefined;
}

export type MetanoChecks = boolean | undefined;
