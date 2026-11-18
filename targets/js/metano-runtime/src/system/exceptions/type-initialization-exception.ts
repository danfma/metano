import { SystemException } from "./system-exception.ts";
import type { ClassType } from "../reflection/runtime-type.ts";

let TypeInitializationException$rt: ClassType<TypeInitializationException> | undefined;

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

  /**
   * The runtime type descriptor of `System.TypeInitializationException`: a data literal built on first read,
   * so an unused class costs nothing and a thrown one pulls in no reflection code.
   */
  static override get $runtimeType(): ClassType<TypeInitializationException> {
    return (TypeInitializationException$rt ??= {
      kind: "class",
      name: "TypeInitializationException",
      fullName: "System.TypeInitializationException",
      namespace: "System",
      ctor: TypeInitializationException,
      base: () => SystemException.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (value: unknown): value is TypeInitializationException =>
        value instanceof TypeInitializationException,
    });
  }
}
