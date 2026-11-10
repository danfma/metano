// Compile-time checks of the `OrDefault` terminal signatures (#601), run by
// `test/system/json/null-root-signatures.test.ts` through `tsgo -p tsconfig.typecheck.json`,
// which covers every `typecheck/` folder. Each `@ts-expect-error` must flag an error, or
// tsgo fails on it as unused.
import {
  firstOrDefault,
  lastOrDefault,
  linq,
  type QueryableMeta,
  singleOrDefault,
} from "#/system/linq";

/** `true` only when `A` and `B` are the same type. */
type Equal<A, B> =
  (<T>() => T extends A ? 1 : 2) extends <T>() => T extends B ? 1 : 2 ? true : false;

function expectType<_ extends true>(): void {}

declare const numbers: number[];
declare const wide: bigint[];
declare const maybes: (number | null)[];
declare const meta: QueryableMeta;

// ─── Without a default value, a miss is null ──────────────────────────────────

const miss = linq(numbers, firstOrDefault());
expectType<Equal<typeof miss, number | null>>();

const lastMiss = linq(numbers, lastOrDefault((x) => x > 1));
expectType<Equal<typeof lastMiss, number | null>>();

const explicitMiss = linq(numbers, singleOrDefault<number>());
expectType<Equal<typeof explicitMiss, number | null>>();

// @ts-expect-error -- the result of a terminal without a default includes null.
export const notANumber: number = linq(numbers, firstOrDefault());

// ─── With a default value, the result is the element type ─────────────────────

const zero = linq(numbers, firstOrDefault(undefined, 0));
expectType<Equal<typeof zero, number>>();

const matched = linq(numbers, lastOrDefault((x) => x > 1, 42));
expectType<Equal<typeof matched, number>>();

const big = linq(wide, singleOrDefault(undefined, 0n));
expectType<Equal<typeof big, bigint>>();

const explicitElement = linq(numbers, firstOrDefault<number>((x) => x > 1, 0));
expectType<Equal<typeof explicitElement, number>>();

const nullableElement = linq(maybes, firstOrDefault(undefined, 42));
expectType<Equal<typeof nullableElement, number | null>>();

// @ts-expect-error -- a default value of another type is rejected.
linq(numbers, firstOrDefault(undefined, "none"));

// ─── The queryable tree stays the third argument ──────────────────────────────

const queried = linq(numbers, firstOrDefault((x) => x > 1, undefined, meta));
expectType<Equal<typeof queried, number | null>>();

const queriedWithDefault = linq(numbers, firstOrDefault((x) => x > 1, 0, meta));
expectType<Equal<typeof queriedWithDefault, number>>();
