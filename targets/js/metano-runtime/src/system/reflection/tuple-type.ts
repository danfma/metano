import { type MemoNode, memoNodeFor } from "../identity-memo.ts";
import type { RuntimeType, TupleType } from "./runtime-type.ts";

let memo: MemoNode<TupleType<unknown[]>> | undefined;

/**
 * A closed `System.ValueTuple<...>` descriptor, memoized on element identities.
 * Value types are invariant in covariant collection type tests (#711).
 */
export function tupleType<T extends readonly unknown[] = unknown[]>(
  elements: ReadonlyArray<RuntimeType>,
): TupleType<T> {
  memo ??= {};

  const node = memoNodeFor(memo, elements);

  node.value ??= {
    kind: "tuple",
    name: `ValueTuple<${elements.map((element) => element.name).join(",")}>`,
    fullName: `System.ValueTuple<${elements.map((element) => element.fullName).join(",")}>`,
    namespace: "System",
    elements,
    default: () => elements.map((element) => element.default?.() ?? null),
    create: () => elements.map((element) => element.default?.() ?? null),
    test: (value: unknown): value is unknown[] =>
      Array.isArray(value) &&
      value.length === elements.length &&
      elements.every((element, index) => element.test(value[index])),
  };

  return node.value as unknown as TupleType<T>;
}
