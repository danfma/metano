import { SystemException } from "./system-exception.ts";

/**
 * Mirrors `System.TypeInitializationException`: a type's static constructor (or one
 * of its static field initializers) threw. The original exception is the inner
 * exception (`cause`). `typeName` is the simple C# name of the type, its containing
 * types dotted (`Outer.Inner`), with no namespace or type arguments.
 */
export class TypeInitializationException extends SystemException {
  override readonly name: string = "TypeInitializationException";
  readonly typeName: string;

  constructor(typeName: string, innerException?: Error | null) {
    super(`The type initializer for '${typeName}' threw an exception.`, innerException);
    this.typeName = typeName;
  }
}
