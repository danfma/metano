import { SystemException } from "./system-exception.ts";
import type { ClassType } from "../reflection/runtime-type.ts";

/** The message `new System.NotSupportedException()` carries on .NET. */
const NOT_SUPPORTED_EXCEPTION_MESSAGE = "Specified method is not supported.";

let NotSupportedException$rt: ClassType<NotSupportedException> | undefined;

/** Mirrors `System.NotSupportedException`: an operation the runtime does not implement. */
export class NotSupportedException extends SystemException {
  override readonly name: string = "NotSupportedException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? NOT_SUPPORTED_EXCEPTION_MESSAGE, innerException);
  }

  /**
   * The runtime type descriptor of `System.NotSupportedException`: a data literal built on first read,
   * so an unused class costs nothing and a thrown one pulls in no reflection code.
   */
  static override get $runtimeType(): ClassType<NotSupportedException> {
    return (NotSupportedException$rt ??= {
      kind: "class",
      name: "NotSupportedException",
      fullName: "System.NotSupportedException",
      namespace: "System",
      ctor: NotSupportedException,
      base: () => SystemException.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new NotSupportedException(),
      test: (value: unknown): value is NotSupportedException =>
        value instanceof NotSupportedException,
    });
  }
}
