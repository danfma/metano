import { ArgumentException } from "./argument-exception.ts";
import type { ClassType } from "../reflection/runtime-type.ts";

/** The message `new System.ArgumentNullException()` carries on .NET. */
const ARGUMENT_NULL_EXCEPTION_MESSAGE = "Value cannot be null.";

let ArgumentNullException$rt: ClassType<ArgumentNullException> | undefined;

/**
 * Mirrors `System.ArgumentNullException`: a `null` argument that a member does
 * not accept. The parameter name comes first, as in .NET's
 * `ArgumentNullException(paramName, message)`.
 */
export class ArgumentNullException extends ArgumentException {
  override readonly name: string = "ArgumentNullException";

  constructor(paramName?: string | null, message?: string | null, innerException?: Error | null) {
    super(message ?? ARGUMENT_NULL_EXCEPTION_MESSAGE, paramName, innerException);
  }

  /**
   * The runtime type descriptor of `System.ArgumentNullException`: a data literal built on first read,
   * so an unused class costs nothing and a thrown one pulls in no reflection code.
   */
  static override get $runtimeType(): ClassType<ArgumentNullException> {
    return (ArgumentNullException$rt ??= {
      kind: "class",
      name: "ArgumentNullException",
      fullName: "System.ArgumentNullException",
      namespace: "System",
      ctor: ArgumentNullException,
      base: () => ArgumentException.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new ArgumentNullException(),
      test: (value: unknown): value is ArgumentNullException =>
        value instanceof ArgumentNullException,
    });
  }
}
