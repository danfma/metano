import { SystemException } from "./system-exception.ts";
import type { ClassType } from "../reflection/runtime-type.ts";

/** The message `new System.InvalidOperationException()` carries on .NET. */
const INVALID_OPERATION_EXCEPTION_MESSAGE =
  "Operation is not valid due to the current state of the object.";

let InvalidOperationException$rt: ClassType<InvalidOperationException> | undefined;

/** Mirrors `System.InvalidOperationException`: a call that the object's state does not allow. */
export class InvalidOperationException extends SystemException {
  override readonly name: string = "InvalidOperationException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? INVALID_OPERATION_EXCEPTION_MESSAGE, innerException);
  }

  /**
   * The runtime type descriptor of `System.InvalidOperationException`: a data literal built on first read,
   * so an unused class costs nothing and a thrown one pulls in no reflection code.
   */
  static override get $runtimeType(): ClassType<InvalidOperationException> {
    return (InvalidOperationException$rt ??= {
      kind: "class",
      name: "InvalidOperationException",
      fullName: "System.InvalidOperationException",
      namespace: "System",
      ctor: InvalidOperationException,
      base: () => SystemException.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new InvalidOperationException(),
      test: (value: unknown): value is InvalidOperationException =>
        value instanceof InvalidOperationException,
    });
  }
}
