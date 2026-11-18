import { Exception } from "./exception.ts";
import type { ClassType } from "../reflection/runtime-type.ts";

/** The message `new System.SystemException()` carries on .NET. */
const SYSTEM_EXCEPTION_MESSAGE = "System error.";

let SystemException$rt: ClassType<SystemException> | undefined;

/** Mirrors `System.SystemException`, the base of the exceptions the runtime raises itself. */
export class SystemException extends Exception {
  override readonly name: string = "SystemException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? SYSTEM_EXCEPTION_MESSAGE, innerException);
  }

  /**
   * The runtime type descriptor of `System.SystemException`: a data literal built on first read,
   * so an unused class costs nothing and a thrown one pulls in no reflection code.
   */
  static override get $runtimeType(): ClassType<SystemException> {
    return (SystemException$rt ??= {
      kind: "class",
      name: "SystemException",
      fullName: "System.SystemException",
      namespace: "System",
      ctor: SystemException,
      base: () => Exception.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new SystemException(),
      test: (value: unknown): value is SystemException => value instanceof SystemException,
    });
  }
}
