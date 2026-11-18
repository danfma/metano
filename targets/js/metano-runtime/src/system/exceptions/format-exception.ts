import { SystemException } from "./system-exception.ts";
import type { ClassType } from "../reflection/runtime-type.ts";

/** The message `new System.FormatException()` carries on .NET. */
const FORMAT_EXCEPTION_MESSAGE = "One of the identified items was in an invalid format.";

let FormatException$rt: ClassType<FormatException> | undefined;

/** Mirrors `System.FormatException`: text or a format string that does not follow its grammar. */
export class FormatException extends SystemException {
  override readonly name: string = "FormatException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? FORMAT_EXCEPTION_MESSAGE, innerException);
  }

  /**
   * The runtime type descriptor of `System.FormatException`: a data literal built on first read,
   * so an unused class costs nothing and a thrown one pulls in no reflection code.
   */
  static override get $runtimeType(): ClassType<FormatException> {
    return (FormatException$rt ??= {
      kind: "class",
      name: "FormatException",
      fullName: "System.FormatException",
      namespace: "System",
      ctor: FormatException,
      base: () => SystemException.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new FormatException(),
      test: (value: unknown): value is FormatException => value instanceof FormatException,
    });
  }
}
