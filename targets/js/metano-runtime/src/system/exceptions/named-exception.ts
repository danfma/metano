import { Exception } from "./exception.ts";

/**
 * An `Exception` standing for a .NET exception type the runtime does not define
 * (`System.TimeoutException` before it was mapped, `System.IO.IOException`, …):
 * the message stays the one C# passed, and `name` / `toString()` report the C#
 * type, as .NET's `GetType().Name` and `ToString()` do. Type tests cannot tell it
 * apart from other exceptions; the compiler reports each construction (MS0001).
 */
export function namedException(
  typeNamespace: string,
  typeName: string,
  message?: string | null,
  innerException?: Error | null,
): Exception {
  const exception = new Exception(message, innerException);

  Object.defineProperty(exception, "name", { value: typeName });
  Object.defineProperty(exception, "typeNamespace", { value: typeNamespace });

  return exception;
}
