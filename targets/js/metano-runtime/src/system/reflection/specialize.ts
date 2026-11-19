import { type MemoNode, memoNodeFor } from "../identity-memo.ts";
import type { ClassType, RuntimeType } from "./runtime-type.ts";

let memo: MemoNode<ClassType<unknown>> | undefined;

/**
 * A closed specialization of an open generic class descriptor (`Box<Int32>`). Memoized on the
 * identity of the open type and of each argument. The test is the open type's for now.
 */
export function specialize<T>(open: ClassType<T>, args: ReadonlyArray<RuntimeType>): ClassType<T> {
  memo ??= {};

  const node = memoNodeFor(memo, [open, ...args]);

  node.value ??= {
    ...open,
    name: `${open.name}<${args.map((arg) => arg.name).join(",")}>`,
    fullName: `${open.fullName}<${args.map((arg) => arg.fullName).join(",")}>`,
    typeArguments: args,
    test: open.test,
  };

  return node.value as ClassType<T>;
}
