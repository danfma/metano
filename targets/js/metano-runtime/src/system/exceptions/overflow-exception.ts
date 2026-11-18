import { ArithmeticException } from "./arithmetic-exception.ts";
import type { ClassType } from "../reflection/runtime-type.ts";

/** The message `new System.OverflowException()` carries on .NET. */
const OVERFLOW_EXCEPTION_MESSAGE = "Arithmetic operation resulted in an overflow.";

let OverflowException$rt: ClassType<OverflowException> | undefined;

/**
 * Mirrors `System.OverflowException`: a checked conversion or operation whose
 * result does not fit its integral type.
 */
export class OverflowException extends ArithmeticException {
  override readonly name: string = "OverflowException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? OVERFLOW_EXCEPTION_MESSAGE, innerException);
  }

  /**
   * The runtime type descriptor of `System.OverflowException`: a data literal built on first read,
   * so an unused class costs nothing and a thrown one pulls in no reflection code.
   */
  static override get $runtimeType(): ClassType<OverflowException> {
    return (OverflowException$rt ??= {
      kind: "class",
      name: "OverflowException",
      fullName: "System.OverflowException",
      namespace: "System",
      ctor: OverflowException,
      base: () => ArithmeticException.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new OverflowException(),
      test: (value: unknown): value is OverflowException => value instanceof OverflowException,
    });
  }
}
