import type { OpaqueType } from "./runtime-type.ts";

let memo: Map<string, OpaqueType> | undefined;

/**
 * A memoized nominal descriptor for concrete types without a descriptor (#711):
 * equal only to itself, never a wildcard, and never matches a runtime value directly.
 */
export function opaqueType(fullName: string): OpaqueType {
  memo ??= new Map();

  const known = memo.get(fullName);

  if (known !== undefined) {
    return known;
  }

  const lastDot = fullName.lastIndexOf(".");
  const created: OpaqueType = {
    kind: "opaque",
    name: lastDot >= 0 ? fullName.slice(lastDot + 1) : fullName,
    fullName,
    namespace: lastDot >= 0 ? fullName.slice(0, lastDot) : "",
    test: (_value: unknown): _value is never => false,
  };

  memo.set(fullName, created);

  return created;
}
