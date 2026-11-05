import { SystemException } from "./system-exception.ts";

/** The message `new System.ArgumentException()` carries on .NET. */
const ARGUMENT_EXCEPTION_MESSAGE = "Value does not fall within the expected range.";

/**
 * Mirrors `System.ArgumentException`: an argument that a member does not
 * accept. As on .NET, a parameter name is appended to the message as
 * ` (Parameter '<name>')`.
 */
export class ArgumentException extends SystemException {
  override readonly name: string = "ArgumentException";
  readonly paramName: string | null;

  constructor(message?: string | null, paramName?: string | null, innerException?: Error | null) {
    super(withParameterName(message ?? ARGUMENT_EXCEPTION_MESSAGE, paramName), innerException);
    this.paramName = paramName ?? null;
  }
}

/** `message` followed by .NET's ` (Parameter '<name>')` suffix when there is a parameter name. */
function withParameterName(message: string, paramName: string | null | undefined): string {
  return paramName == null ? message : `${message} (Parameter '${paramName}')`;
}
