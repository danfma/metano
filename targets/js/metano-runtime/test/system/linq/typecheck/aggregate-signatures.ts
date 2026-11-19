// Compile-time checks of the `Min`, `Max`, `MinBy`, `Sum` and `Average` terminal signatures
// (#618, #657), run by `test/typecheck-fixtures.test.ts` through
// `tsgo -p tsconfig.typecheck.json`.
import Decimal from "decimal.js";

import {
  averageDecimal,
  averageNullableDecimal,
  averageNullable,
  linq,
  max,
  maxNullable,
  min,
  minByNullable,
  minNullable,
  sum,
  sumDecimal,
} from "#/system/linq";

import { type Equal, expectType } from "../../../typecheck/type-assertions.ts";

declare const numbers: number[];
declare const maybes: (number | null)[];
declare const words: string[];
declare const amounts: Decimal[];
declare const people: { name: string; age: number | null }[];

// ─── The result is the type of the values ─────────────────────────────────────

const smallest = linq(numbers, min());
expectType<Equal<typeof smallest, number>>();

const earliest = linq(words, min());
expectType<Equal<typeof earliest, string>>();

const lowest = linq(amounts, max());
expectType<Equal<typeof lowest, Decimal>>();

const byName = linq(
  people,
  min((p) => p.name),
);

expectType<Equal<typeof byName, string>>();

// ─── A nullable terminal adds null and drops it from the values ───────────────

const nullable = linq(maybes, minNullable());
expectType<Equal<typeof nullable, number | null>>();

const nullableName = linq(
  people,
  maxNullable((p) => p.name),
);

expectType<Equal<typeof nullableName, string | null>>();

const youngest = linq(
  people,
  minByNullable((p) => p.age),
);

expectType<Equal<typeof youngest, { name: string; age: number | null } | null>>();

// ─── Sums and averages ────────────────────────────────────────────────────────

const total = linq(maybes, sum());
expectType<Equal<typeof total, number>>();

const mean = linq(maybes, averageNullable());
expectType<Equal<typeof mean, number | null>>();

const decimalTotal = linq(amounts, sumDecimal(Decimal));
expectType<Equal<typeof decimalTotal, Decimal>>();

const decimalMean = linq(amounts, averageDecimal());
expectType<Equal<typeof decimalMean, Decimal>>();

declare const maybeAmounts: (Decimal | null)[];

const nullableDecimalMean = linq(maybeAmounts, averageNullableDecimal());
expectType<Equal<typeof nullableDecimalMean, Decimal | null>>();

const selectedDecimalMean = linq(
  people,
  averageNullableDecimal(() => null as Decimal | null),
);

expectType<Equal<typeof selectedDecimalMean, Decimal | null>>();
