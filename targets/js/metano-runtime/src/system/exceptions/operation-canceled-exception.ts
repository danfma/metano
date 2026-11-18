import { SystemException } from "./system-exception.ts";
import type { ClassType } from "../reflection/runtime-type.ts";

/** The message `new System.OperationCanceledException()` carries on .NET. */
const OPERATION_CANCELED_EXCEPTION_MESSAGE = "The operation was canceled.";

let OperationCanceledException$rt: ClassType<OperationCanceledException> | undefined;

/** Mirrors `System.OperationCanceledException`: an operation stopped by its cancellation. */
export class OperationCanceledException extends SystemException {
  override readonly name: string = "OperationCanceledException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? OPERATION_CANCELED_EXCEPTION_MESSAGE, innerException);
  }

  /**
   * The runtime type descriptor of `System.OperationCanceledException`: a data literal built on first read,
   * so an unused class costs nothing and a thrown one pulls in no reflection code.
   */
  static override get $runtimeType(): ClassType<OperationCanceledException> {
    return (OperationCanceledException$rt ??= {
      kind: "class",
      name: "OperationCanceledException",
      fullName: "System.OperationCanceledException",
      namespace: "System",
      ctor: OperationCanceledException,
      base: () => SystemException.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new OperationCanceledException(),
      test: (value: unknown): value is OperationCanceledException =>
        value instanceof OperationCanceledException,
    });
  }
}
