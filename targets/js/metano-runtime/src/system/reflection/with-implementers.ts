import { hasPlainPrototype } from "./constructor-descriptor.ts";
import { type MemoNode, memoNodeFor } from "../identity-memo.ts";
import type { InterfaceType, PlainObjectType } from "./runtime-type.ts";

let memo: MemoNode<InterfaceType<unknown>> | undefined;

/**
 * The interface descriptor with extra `[PlainObject]` implementers (a package that declares a
 * plain object implementing an interface of another package). Memoized on the identity of the
 * interface and of each implementer. Class instances still match the ORIGINAL interface
 * identity; only the plain-object fallback sees the added implementers.
 */
export function withImplementers<T>(
  iface: InterfaceType<T>,
  implementers: ReadonlyArray<PlainObjectType<unknown>>,
): InterfaceType<T> {
  memo ??= {};

  const node = memoNodeFor(memo, [iface, ...implementers]);

  node.value ??= {
    ...iface,
    plainImplementers: [...iface.plainImplementers, ...implementers.map((plain) => () => plain)],
    test: (value: unknown): value is T =>
      iface.test(value) ||
      (value !== null &&
        typeof value === "object" &&
        hasPlainPrototype(value) &&
        implementers.some((plain) => plain.test(value))),
  };

  return node.value as InterfaceType<T>;
}
