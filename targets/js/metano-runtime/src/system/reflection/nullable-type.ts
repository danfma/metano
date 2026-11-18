import type { NullableType, RuntimeType } from "./runtime-type.ts";

let memo: WeakMap<RuntimeType<never>, NullableType<unknown>> | undefined;

/** `T?`: the descriptor of the inner type or `null`. Memoized, so `nullableType(x) === nullableType(x)`. */
export function nullableType<T>(inner: RuntimeType<T>): NullableType<T> {
  memo ??= new WeakMap();

  const known = memo.get(inner as RuntimeType<never>);

  if (known !== undefined) {
    return known as NullableType<T>;
  }

  const created: NullableType<T> = {
    kind: "nullable",
    name: `Nullable<${inner.name}>`,
    fullName: `System.Nullable<${inner.fullName}>`,
    namespace: "System",
    inner: () => inner,
    test: (value: unknown): value is T | null => value === null || inner.test(value),
  };

  memo.set(inner as RuntimeType<never>, created as NullableType<unknown>);

  return created;
}
