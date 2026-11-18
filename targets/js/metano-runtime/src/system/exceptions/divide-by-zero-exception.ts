import { ArithmeticException } from "./arithmetic-exception.ts";
import type { ClassType } from "../reflection/runtime-type.ts";

/** The message `new System.DivideByZeroException()` carries on .NET. */
const DIVIDE_BY_ZERO_EXCEPTION_MESSAGE = "Attempted to divide by zero.";

let DivideByZeroException$rt: ClassType<DivideByZeroException> | undefined;

/** Mirrors `System.DivideByZeroException`: an integral or decimal division by zero. */
export class DivideByZeroException extends ArithmeticException {
  override readonly name: string = "DivideByZeroException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? DIVIDE_BY_ZERO_EXCEPTION_MESSAGE, innerException);
  }

  /**
   * The runtime type descriptor of `System.DivideByZeroException`: a data literal built on first read,
   * so an unused class costs nothing and a thrown one pulls in no reflection code.
   */
  static override get $runtimeType(): ClassType<DivideByZeroException> {
    return (DivideByZeroException$rt ??= {
      kind: "class",
      name: "DivideByZeroException",
      fullName: "System.DivideByZeroException",
      namespace: "System",
      ctor: DivideByZeroException,
      base: () => ArithmeticException.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new DivideByZeroException(),
      test: (value: unknown): value is DivideByZeroException =>
        value instanceof DivideByZeroException,
    });
  }
}
