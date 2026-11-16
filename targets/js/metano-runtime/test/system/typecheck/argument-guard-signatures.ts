// Compile-time checks of the argument guards' `asserts` signatures (#651), run by
// `test/typecheck-fixtures.test.ts` through `tsgo -p tsconfig.typecheck.json`: the code
// after a guard reads its argument as non-null, as the C# after the guard does.
import { throwIfNull, throwIfNullOrEmpty, throwIfNullOrWhiteSpace } from "#/system/argument-guards";

export function lengthAfterNullGuard(text: string | null): number {
  throwIfNull(text, "text");

  return text.length;
}

export function lengthAfterEmptyGuard(text: string | null | undefined): number {
  throwIfNullOrEmpty(text, "text");

  return text.length;
}

export function lengthAfterWhiteSpaceGuard(text: string | null | undefined): number {
  throwIfNullOrWhiteSpace(text, "text");

  return text.length;
}

export function lengthWithoutGuard(text: string | null): number {
  // @ts-expect-error -- without a guard the argument may be null.
  return text.length;
}
