import { SystemException } from "./system-exception.ts";

/**
 * Mirrors `System.TypeInitializationException`: a type's static constructor (or one
 * of its static field initializers) threw. The original exception is the inner
 * exception (`cause`). `typeName` is the name .NET reports: the namespace-qualified
 * metadata name of a top-level type (`MyApp.Pair\`1`, no type arguments), and only the
 * metadata name of a nested type (`Inner`).
 */
export class TypeInitializationException extends SystemException {
  override readonly name: string = "TypeInitializationException";
  readonly typeName: string;

  constructor(typeName: string, innerException?: Error | null) {
    super(`The type initializer for '${typeName}' threw an exception.`, innerException);
    this.typeName = typeName;
  }
}
