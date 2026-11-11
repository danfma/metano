import { SystemException } from "./system-exception.ts";

/** The message `new System.Collections.Generic.KeyNotFoundException()` carries on .NET. */
const KEY_NOT_FOUND_EXCEPTION_MESSAGE = "The given key was not present in the dictionary.";

/** Mirrors `System.Collections.Generic.KeyNotFoundException`: a key that a dictionary does not contain. */
export class KeyNotFoundException extends SystemException {
  override readonly name: string = "KeyNotFoundException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? KEY_NOT_FOUND_EXCEPTION_MESSAGE, innerException);
  }

  protected override get typeNamespace(): string {
    return "System.Collections.Generic";
  }
}
