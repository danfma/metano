import { Exception } from "./exception.ts";

/**
 * The `Exception` standing for a .NET exception type the runtime does not define
 * (`System.IO.IOException`, …): the message stays the one C# passed, and `name` /
 * `toString()` report the C# type, as .NET's `GetType().Name` and `ToString()` do.
 * Type tests cannot tell it apart from other exceptions; the compiler reports each
 * construction (MS0001). Module-internal: generated code builds it through
 * {@link namedException}.
 */
class NamedException extends Exception {
  override readonly name: string;
  readonly #typeNamespace: string;

  constructor(
    typeNamespace: string,
    typeName: string,
    message?: string | null,
    innerException?: Error | null,
  ) {
    super(message, innerException);
    this.name = typeName;
    this.#typeNamespace = typeNamespace;
  }

  protected override get $typeNamespace(): string {
    return this.#typeNamespace;
  }
}

/**
 * `new <typeNamespace>.<typeName>(message, innerException)` for a .NET exception
 * type the runtime does not define: an {@link Exception} that keeps the message
 * and reports the C# type as its identity.
 */
export function namedException(
  typeNamespace: string,
  typeName: string,
  message?: string | null,
  innerException?: Error | null,
): Exception {
  return new NamedException(typeNamespace, typeName, message, innerException);
}
