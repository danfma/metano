import { SystemException } from "./system-exception.ts";
import type { ClassType } from "../reflection/runtime-type.ts";

/** The message `new System.TimeoutException()` carries on .NET. */
const TIMEOUT_EXCEPTION_MESSAGE = "The operation has timed out.";

let TimeoutException$rt: ClassType<TimeoutException> | undefined;

/** Mirrors `System.TimeoutException`: an operation that did not finish in its allotted time. */
export class TimeoutException extends SystemException {
  override readonly name: string = "TimeoutException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? TIMEOUT_EXCEPTION_MESSAGE, innerException);
  }

  /**
   * The runtime type descriptor of `System.TimeoutException`: a data literal built on first read,
   * so an unused class costs nothing and a thrown one pulls in no reflection code.
   */
  static override get $runtimeType(): ClassType<TimeoutException> {
    return (TimeoutException$rt ??= {
      kind: "class",
      name: "TimeoutException",
      fullName: "System.TimeoutException",
      namespace: "System",
      ctor: TimeoutException,
      base: () => SystemException.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new TimeoutException(),
      test: (value: unknown): value is TimeoutException => value instanceof TimeoutException,
    });
  }
}
