import { NotSupportedException } from "../exceptions/not-supported-exception.ts";
import type { RuntimeType } from "./runtime-type.ts";

let cache: Map<string, RuntimeType<never>> | undefined;

/**
 * A memoized descriptor for a type whose runtime test is unsupported in TypeScript
 * (tuples, delegates, raw arrays, non-`[Transpile]` types, etc.). Calling `test`
 * throws {@link NotSupportedException} rather than silently matching.
 */
export function unsupportedType(fullName: string): RuntimeType<never> {
  cache ??= new Map();

  let descriptor = cache.get(fullName);

  if (descriptor === undefined) {
    descriptor = {
      kind: "opaque",
      name: fullName,
      fullName,
      namespace: "",
      test: (value: unknown): value is never => {
        if (value === null || value === undefined) {
          return false;
        }
        throw new NotSupportedException(
          `Runtime type descriptor is not available for '${fullName}'.`,
        );
      },
    };
    cache.set(fullName, descriptor);
  }

  return descriptor;
}
