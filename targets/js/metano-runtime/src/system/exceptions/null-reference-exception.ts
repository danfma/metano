import { SystemException } from "./system-exception.ts";
import type { ClassType } from "../reflection/runtime-type.ts";

/** The message `new System.NullReferenceException()` carries on .NET. */
const NULL_REFERENCE_EXCEPTION_MESSAGE = "Object reference not set to an instance of an object.";

let NullReferenceException$rt: ClassType<NullReferenceException> | undefined;

/** Mirrors `System.NullReferenceException`: a member access on `null`. */
export class NullReferenceException extends SystemException {
  override readonly name: string = "NullReferenceException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? NULL_REFERENCE_EXCEPTION_MESSAGE, innerException);
  }

  /**
   * The runtime type descriptor of `System.NullReferenceException`: a data literal built on first read,
   * so an unused class costs nothing and a thrown one pulls in no reflection code.
   */
  static override get $runtimeType(): ClassType<NullReferenceException> {
    return (NullReferenceException$rt ??= {
      kind: "class",
      name: "NullReferenceException",
      fullName: "System.NullReferenceException",
      namespace: "System",
      ctor: NullReferenceException,
      base: () => SystemException.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new NullReferenceException(),
      test: (value: unknown): value is NullReferenceException =>
        value instanceof NullReferenceException,
    });
  }
}
