// Type-level assertions shared by the `typecheck/` fixtures, which
// `test/typecheck-fixtures.test.ts` checks through `tsgo -p tsconfig.typecheck.json`.

/** `true` only when `A` and `B` are the same type. */
export type Equal<A, B> =
  (<T>() => T extends A ? 1 : 2) extends <T>() => T extends B ? 1 : 2 ? true : false;

/** Compiles only when its type argument is `true`: `expectType<Equal<A, B>>()`. */
export function expectType<_ extends true>(): void {}
