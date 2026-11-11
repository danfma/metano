import { SystemException } from "./system-exception.ts";

/** The message `new System.IndexOutOfRangeException()` carries on .NET. */
const INDEX_OUT_OF_RANGE_EXCEPTION_MESSAGE = "Index was outside the bounds of the array.";

/** Mirrors `System.IndexOutOfRangeException`: an array index outside its bounds. */
export class IndexOutOfRangeException extends SystemException {
  override readonly name: string = "IndexOutOfRangeException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? INDEX_OUT_OF_RANGE_EXCEPTION_MESSAGE, innerException);
  }
}
