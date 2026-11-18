import { InvalidOperationException } from "./invalid-operation-exception.ts";
import type { ClassType } from "../reflection/runtime-type.ts";

/** The message `new System.ObjectDisposedException(objectName)` carries on .NET. */
const OBJECT_DISPOSED_EXCEPTION_MESSAGE = "Cannot access a disposed object.";

let ObjectDisposedException$rt: ClassType<ObjectDisposedException> | undefined;

/**
 * Mirrors `System.ObjectDisposedException`: a call on an object that was
 * disposed. As on .NET, a non-empty object name is appended to the message on
 * a new line (`Object name: '<name>'.`). The object name comes first, as in
 * .NET's `ObjectDisposedException(objectName, message)`.
 */
export class ObjectDisposedException extends InvalidOperationException {
  override readonly name: string = "ObjectDisposedException";
  readonly objectName: string;

  constructor(objectName?: string | null, message?: string | null, innerException?: Error | null) {
    super(withObjectName(message ?? OBJECT_DISPOSED_EXCEPTION_MESSAGE, objectName), innerException);
    this.objectName = objectName ?? "";
  }

  /**
   * The runtime type descriptor of `System.ObjectDisposedException`: a data literal built on first read,
   * so an unused class costs nothing and a thrown one pulls in no reflection code.
   */
  static override get $runtimeType(): ClassType<ObjectDisposedException> {
    return (ObjectDisposedException$rt ??= {
      kind: "class",
      name: "ObjectDisposedException",
      fullName: "System.ObjectDisposedException",
      namespace: "System",
      ctor: ObjectDisposedException,
      base: () => InvalidOperationException.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (value: unknown): value is ObjectDisposedException =>
        value instanceof ObjectDisposedException,
    });
  }
}

/** `message` followed by .NET's `\nObject name: '<name>'.` line when there is an object name. */
function withObjectName(message: string, objectName: string | null | undefined): string {
  return objectName ? `${message}\nObject name: '${objectName}'.` : message;
}
