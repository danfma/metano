import { SystemException } from "./system-exception.ts";
import type { ClassType } from "../reflection/runtime-type.ts";

/** The message `new System.MissingMethodException()` carries on .NET. */
const MISSING_METHOD_EXCEPTION_MESSAGE = "Attempted to access a missing method.";

let MissingMethodException$rt: ClassType<MissingMethodException> | undefined;

/**
 * Mirrors `System.MissingMethodException`: a member that does not exist at run
 * time. .NET derives it from `MissingMemberException` and
 * `MemberAccessException`, which the runtime does not define, so it extends
 * `SystemException` directly.
 */
export class MissingMethodException extends SystemException {
  override readonly name: string = "MissingMethodException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? MISSING_METHOD_EXCEPTION_MESSAGE, innerException);
  }

  /**
   * The runtime type descriptor of `System.MissingMethodException`: a data literal built on first read,
   * so an unused class costs nothing and a thrown one pulls in no reflection code. Its
   * `base` is `SystemException`, skipping .NET's `MissingMemberException` and
   * `MemberAccessException`, which the runtime does not define (as `instanceof` does).
   */
  static override get $runtimeType(): ClassType<MissingMethodException> {
    return (MissingMethodException$rt ??= {
      kind: "class",
      name: "MissingMethodException",
      fullName: "System.MissingMethodException",
      namespace: "System",
      ctor: MissingMethodException,
      base: () => SystemException.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new MissingMethodException(),
      test: (value: unknown): value is MissingMethodException =>
        value instanceof MissingMethodException,
    });
  }
}
