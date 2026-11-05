import { SystemException } from "./system-exception.ts";

/** The message `new System.FormatException()` carries on .NET. */
const FORMAT_EXCEPTION_MESSAGE = "One of the identified items was in an invalid format.";

/** Mirrors `System.FormatException`: text or a format string that does not follow its grammar. */
export class FormatException extends SystemException {
  override readonly name: string = "FormatException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? FORMAT_EXCEPTION_MESSAGE, innerException);
  }
}
