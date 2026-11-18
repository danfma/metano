import type { ClassType } from "../reflection/runtime-type.ts";

/** The message `new System.Exception()` carries on .NET. */
const EXCEPTION_MESSAGE = "Exception of type 'System.Exception' was thrown.";

/** The line .NET's `Exception.ToString()` writes after an inner exception. */
const END_OF_INNER_EXCEPTION = "   --- End of inner exception stack trace ---";

let Exception$rt: ClassType<Exception> | undefined;

/**
 * Mirrors `System.Exception`, the root of every runtime exception that
 * generated code throws or catches. A `null` or missing message falls back to
 * the .NET default, and the inner exception is also exposed as the standard
 * `cause`, so JavaScript tooling shows the chain.
 */
export class Exception extends Error {
  override readonly name: string = "Exception";
  readonly innerException: Error | null;

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? EXCEPTION_MESSAGE, errorOptionsOf(innerException));
    this.innerException = innerException ?? null;
  }

  /**
   * The .NET namespace of the class: `System`, unless a class declared in
   * another namespace overrides it. Generated exception classes override it and
   * {@link $typeName} with their C# namespace and type name. The `$` keeps the
   * hooks apart from any member a C# class can declare.
   */
  protected get $typeNamespace(): string {
    return "System";
  }

  /** The .NET type name of the class: its `name`, unless a generated class overrides it. */
  protected get $typeName(): string {
    return this.name;
  }

  /** The JavaScript `stack`, which plays the role of .NET's `StackTrace`. */
  get stackTrace(): string | null {
    return this.stack ?? null;
  }

  /**
   * `System.<Name>: <message>`, followed by the inner exception as .NET writes
   * it. The stack trace is not appended (see {@link stackTrace}).
   */
  override toString(): string {
    const typeNamespace = this.$typeNamespace;
    const fullName = typeNamespace ? `${typeNamespace}.${this.$typeName}` : this.$typeName;
    const head = this.message ? `${fullName}: ${this.message}` : fullName;

    if (this.innerException == null) {
      return head;
    }

    return `${head}\n ---> ${this.innerException.toString()}\n${END_OF_INNER_EXCEPTION}`;
  }

  /**
   * The runtime type descriptor of `System.Exception`: a data literal built on first read,
   * so an unused class costs nothing and a thrown one pulls in no reflection code.
   * Every runtime exception class declares its own; a static getter is inherited,
   * though, so a class without one (the module-internal `NamedException`) answers
   * its base's descriptor. Read a value's descriptor through its constructor's own
   * property (`Object.hasOwn(ctor, "$runtimeType")`, as `getRuntimeType` does),
   * never through `X.$runtimeType` on an arbitrary class.
   */
  static get $runtimeType(): ClassType<Exception> {
    return (Exception$rt ??= {
      kind: "class",
      name: "Exception",
      fullName: "System.Exception",
      namespace: "System",
      ctor: Exception,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new Exception(),
      test: (value: unknown): value is Exception => value instanceof Exception,
    });
  }
}

/** The standard `cause` option, present only when there is an inner exception. */
function errorOptionsOf(innerException: Error | null | undefined): ErrorOptions | undefined {
  return innerException == null ? undefined : { cause: innerException };
}
