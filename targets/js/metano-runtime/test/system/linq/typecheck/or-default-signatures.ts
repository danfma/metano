// Compile-time checks of the `OrDefault` terminal signatures (#601), run by
// `test/typecheck-fixtures.test.ts` through `tsgo -p tsconfig.typecheck.json`. Each
// `@ts-expect-error` must flag an error, or tsgo fails on it as unused.
import {
  firstOrDefault,
  lastOrDefault,
  linq,
  type QueryableMeta,
  singleOrDefault,
} from "#/system/linq";

import { type Equal, expectType } from "../../../typecheck/type-assertions.ts";

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
const notANumber: number = linq(numbers, firstOrDefault());

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

// ─── A nullable default value keeps null in the result ────────────────────────

declare const maybeDefault: number | null;

const nullableDefault = linq(numbers, firstOrDefault((x) => x > 1, maybeDefault));
expectType<Equal<typeof nullableDefault, number | null>>();

const nullableLast = linq(numbers, lastOrDefault(undefined, maybeDefault));
expectType<Equal<typeof nullableLast, number | null>>();

const nullableSingle = linq(numbers, singleOrDefault(undefined, maybeDefault));
expectType<Equal<typeof nullableSingle, number | null>>();

// ─── An optional default value yields null when it is missing ─────────────────

function pick(fallback?: number) {
  return linq(numbers, firstOrDefault((x) => x > 1, fallback));
}

expectType<Equal<ReturnType<typeof pick>, number | null>>();

declare const undefinedDefault: number | undefined;

const optionalLast = linq(numbers, lastOrDefault(undefined, undefinedDefault));
expectType<Equal<typeof optionalLast, number | null>>();

declare const looseDefault: number | null | undefined;

const looseSingle = linq(numbers, singleOrDefault((x) => x > 1, looseDefault));
expectType<Equal<typeof looseSingle, number | null>>();

// ─── A standalone term takes its element type from a type argument ────────────

const detached = firstOrDefault(undefined, 0);
expectType<Equal<ReturnType<typeof detached.apply>, unknown>>();

const typedDetached = firstOrDefault<number>(undefined, 0);
expectType<Equal<ReturnType<typeof typedDetached.apply>, number>>();

// ─── Known gap: a default value wider than the element type ───────────────────

interface Animal {
  readonly name: string;
}

interface Dog extends Animal {
  readonly breed: string;
}

declare const dogs: Dog[];
declare const fallback: Animal;

// @ts-expect-error -- C# widens TSource to Animal; the term takes its element type from `dogs`.
linq(dogs, firstOrDefault(undefined, fallback));

const widened = linq(dogs, firstOrDefault<Animal>(undefined, fallback));
expectType<Equal<typeof widened, Animal>>();

// ─── The queryable tree stays the third argument ──────────────────────────────

const queried = linq(numbers, firstOrDefault((x) => x > 1, undefined, meta));
expectType<Equal<typeof queried, number | null>>();

const queriedWithDefault = linq(numbers, firstOrDefault((x) => x > 1, 0, meta));
expectType<Equal<typeof queriedWithDefault, number>>();
