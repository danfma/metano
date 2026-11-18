import { SystemException } from "./system-exception.ts";
import type { ClassType } from "../reflection/runtime-type.ts";

/** The message `new System.ArgumentException()` carries on .NET. */
const ARGUMENT_EXCEPTION_MESSAGE = "Value does not fall within the expected range.";

let ArgumentException$rt: ClassType<ArgumentException> | undefined;

/**
 * Mirrors `System.ArgumentException`: an argument that a member does not
 * accept. As on .NET, a parameter name is appended to the message as
 * ` (Parameter '<name>')`.
 */
export class ArgumentException extends SystemException {
  override readonly name: string = "ArgumentException";
  readonly paramName: string | null;

  constructor(message?: string | null, paramName?: string | null, innerException?: Error | null) {
    super(withParameterName(message ?? ARGUMENT_EXCEPTION_MESSAGE, paramName), innerException);
    this.paramName = paramName ?? null;
  }

  /**
   * The runtime type descriptor of `System.ArgumentException`: a data literal built on first read,
   * so an unused class costs nothing and a thrown one pulls in no reflection code.
   */
  static override get $runtimeType(): ClassType<ArgumentException> {
    return (ArgumentException$rt ??= {
      kind: "class",
      name: "ArgumentException",
      fullName: "System.ArgumentException",
      namespace: "System",
      ctor: ArgumentException,
      base: () => SystemException.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new ArgumentException(),
      test: (value: unknown): value is ArgumentException => value instanceof ArgumentException,
    });
  }
}

/**
 * `message` followed by .NET's ` (Parameter '<name>')` suffix when there is a
 * parameter name; a `null` or empty name adds nothing, as on .NET.
 */
function withParameterName(message: string, paramName: string | null | undefined): string {
  return paramName ? `${message} (Parameter '${paramName}')` : message;
}
