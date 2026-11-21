import { type MemoNode, memoNodeFor } from "../identity-memo.ts";
import type { ClassType, RuntimeType } from "./runtime-type.ts";

let memo: MemoNode<ClassType<unknown>> | undefined;

/**
 * A closed specialization of an open generic class descriptor (`Box<Int32>`). Memoized on the
 * identity of the open type and of each argument. Checks the instance's `open.slot` when
 * declared and forwards `args` to `open.create` when present.
 */
export function specialize<T>(open: ClassType<T>, args: ReadonlyArray<RuntimeType>): ClassType<T> {
  memo ??= {};

  const node = memoNodeFor(memo, [open, ...args]);

  node.value ??= {
    ...open,
    name: `${open.name}<${args.map((arg) => arg.name).join(",")}>`,
    fullName: `${open.fullName}<${args.map((arg) => arg.fullName).join(",")}>`,
    typeArguments: args,
    ...(open.create !== undefined ? { create: () => open.create!(args) } : {}),
    test: (value: unknown): value is T =>
      open.test(value) &&
      (open.slot === undefined ||
        slotMatches(
          (value as Record<string, ReadonlyArray<RuntimeType> | undefined>)[open.slot],
          args,
        )),
  };

  return node.value as ClassType<T>;
}

function slotMatches(
  slot: ReadonlyArray<RuntimeType> | undefined,
  args: ReadonlyArray<RuntimeType>,
): boolean {
  return (
    slot === undefined ||
    args.every((expected, index) => {
      const actual = slot[index];

      return (
        actual === undefined ||
        actual === expected ||
        actual.kind === "unknown" ||
        expected.kind === "unknown"
      );
    })
  );
}
