// Compile-time checks of the `ElementAt`, `ElementAtOrDefault` and `DefaultIfEmpty` signatures
// (#617), run by `test/typecheck-fixtures.test.ts` through `tsgo -p tsconfig.typecheck.json`.
import { defaultIfEmpty, elementAt, elementAtOrDefault, linq, toArray } from "#/system/linq";

import { type Equal, expectType } from "../../../typecheck/type-assertions.ts";

declare const numbers: number[];
declare const words: string[];

const second = linq(numbers, elementAt(1));
expectType<Equal<typeof second, number>>();

const missOfNull = linq(words, elementAtOrDefault(9));
expectType<Equal<typeof missOfNull, string | null>>();

const missOfZero = linq(numbers, elementAtOrDefault(9, 0));
expectType<Equal<typeof missOfZero, number>>();

const padded = linq(numbers, defaultIfEmpty(0), toArray());
expectType<Equal<typeof padded, number[]>>();

const nullPadded = linq(words, defaultIfEmpty(), toArray());
expectType<Equal<typeof nullPadded, (string | null)[]>>();
