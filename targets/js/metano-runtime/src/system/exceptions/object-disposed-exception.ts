import { InvalidOperationException } from "./invalid-operation-exception.ts";

/** The message `new System.ObjectDisposedException(objectName)` carries on .NET. */
const OBJECT_DISPOSED_EXCEPTION_MESSAGE = "Cannot access a disposed object.";

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
}

/** `message` followed by .NET's `\nObject name: '<name>'.` line when there is an object name. */
function withObjectName(message: string, objectName: string | null | undefined): string {
  return objectName ? `${message}\nObject name: '${objectName}'.` : message;
}
