import { Exception } from "./exception.ts";
import type { ClassType } from "../reflection/runtime-type.ts";

/** The message `new System.Text.Json.JsonException()` carries on .NET. */
const JSON_EXCEPTION_MESSAGE = "Exception of type 'System.Text.Json.JsonException' was thrown.";

let JsonException$rt: ClassType<JsonException> | undefined;

/**
 * Mirrors `System.Text.Json.JsonException`: JSON that does not match its
 * contract — a value of the wrong JSON kind, a missing required property, an
 * invalid enum value, or a document nested deeper than the maximum depth.
 * Messages carry no `Path` / `LineNumber` suffix (spec 009 DECIDED-5).
 */
export class JsonException extends Exception {
  override readonly name: string = "JsonException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? JSON_EXCEPTION_MESSAGE, innerException);
  }

  protected override get $typeNamespace(): string {
    return "System.Text.Json";
  }

  /**
   * The runtime type descriptor of `System.Text.Json.JsonException`: a data literal built on first read,
   * so an unused class costs nothing and a thrown one pulls in no reflection code.
   */
  static override get $runtimeType(): ClassType<JsonException> {
    return (JsonException$rt ??= {
      kind: "class",
      name: "JsonException",
      fullName: "System.Text.Json.JsonException",
      namespace: "System.Text.Json",
      ctor: JsonException,
      base: () => Exception.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new JsonException(),
      test: (value: unknown): value is JsonException => value instanceof JsonException,
    });
  }
}
