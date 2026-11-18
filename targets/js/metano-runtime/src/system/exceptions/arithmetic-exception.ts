import { SystemException } from "./system-exception.ts";
import type { ClassType } from "../reflection/runtime-type.ts";

/** The message `new System.ArithmeticException()` carries on .NET. */
const ARITHMETIC_EXCEPTION_MESSAGE = "Overflow or underflow in the arithmetic operation.";

let ArithmeticException$rt: ClassType<ArithmeticException> | undefined;

/** Mirrors `System.ArithmeticException`, the base of overflow and division errors. */
export class ArithmeticException extends SystemException {
  override readonly name: string = "ArithmeticException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? ARITHMETIC_EXCEPTION_MESSAGE, innerException);
  }

  /**
   * The runtime type descriptor of `System.ArithmeticException`: a data literal built on first read,
   * so an unused class costs nothing and a thrown one pulls in no reflection code.
   */
  static override get $runtimeType(): ClassType<ArithmeticException> {
    return (ArithmeticException$rt ??= {
      kind: "class",
      name: "ArithmeticException",
      fullName: "System.ArithmeticException",
      namespace: "System",
      ctor: ArithmeticException,
      base: () => SystemException.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new ArithmeticException(),
      test: (value: unknown): value is ArithmeticException => value instanceof ArithmeticException,
    });
  }
}
