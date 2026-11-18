import { SystemException } from "./system-exception.ts";
import type { ClassType } from "../reflection/runtime-type.ts";

/** The message `new System.NotImplementedException()` carries on .NET. */
const NOT_IMPLEMENTED_EXCEPTION_MESSAGE = "The method or operation is not implemented.";

let NotImplementedException$rt: ClassType<NotImplementedException> | undefined;

/** Mirrors `System.NotImplementedException`: a member that has no implementation yet. */
export class NotImplementedException extends SystemException {
  override readonly name: string = "NotImplementedException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? NOT_IMPLEMENTED_EXCEPTION_MESSAGE, innerException);
  }

  /**
   * The runtime type descriptor of `System.NotImplementedException`: a data literal built on first read,
   * so an unused class costs nothing and a thrown one pulls in no reflection code.
   */
  static override get $runtimeType(): ClassType<NotImplementedException> {
    return (NotImplementedException$rt ??= {
      kind: "class",
      name: "NotImplementedException",
      fullName: "System.NotImplementedException",
      namespace: "System",
      ctor: NotImplementedException,
      base: () => SystemException.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new NotImplementedException(),
      test: (value: unknown): value is NotImplementedException =>
        value instanceof NotImplementedException,
    });
  }
}
