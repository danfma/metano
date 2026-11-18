import { SystemException } from "./system-exception.ts";
import type { ClassType } from "../reflection/runtime-type.ts";

/** The message `new System.InvalidCastException()` carries on .NET. */
const INVALID_CAST_EXCEPTION_MESSAGE = "Specified cast is not valid.";

let InvalidCastException$rt: ClassType<InvalidCastException> | undefined;

/** Mirrors `System.InvalidCastException`: a value that cannot be converted to the requested type. */
export class InvalidCastException extends SystemException {
  override readonly name: string = "InvalidCastException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? INVALID_CAST_EXCEPTION_MESSAGE, innerException);
  }

  /**
   * The runtime type descriptor of `System.InvalidCastException`: a data literal built on first read,
   * so an unused class costs nothing and a thrown one pulls in no reflection code.
   */
  static override get $runtimeType(): ClassType<InvalidCastException> {
    return (InvalidCastException$rt ??= {
      kind: "class",
      name: "InvalidCastException",
      fullName: "System.InvalidCastException",
      namespace: "System",
      ctor: InvalidCastException,
      base: () => SystemException.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new InvalidCastException(),
      test: (value: unknown): value is InvalidCastException =>
        value instanceof InvalidCastException,
    });
  }
}
