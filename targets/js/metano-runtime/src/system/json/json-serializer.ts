import type { TypeSpec, ValueSpec, JsonTypeInfo, JsonConverter } from "./types.ts";
import type { SerializerContext } from "./serializer-context.ts";
import {
  serializeWithSpec,
  deserializeWithSpec,
  serializeWithValueSpec,
  deserializeWithValueSpec,
  type TreeNumbers,
} from "./converters.ts";
import { specNeedsExactNumbers } from "./exact-number-reads.ts";
import { mayHoldInexactNumber, parseJsonText, readJsonText } from "./json-text-reader.ts";

/** A `ValueSpec` carries a `descriptor`; a `TypeSpec` never does. */
function isValueSpec<T>(spec: JsonTypeInfo<T>): spec is ValueSpec<T> {
  return "descriptor" in spec;
}

/**
 * Serializes through either spec kind: an object contract walks its
 * properties, a value root converts through its descriptor exactly as a
 * property of that type would (custom converters still match by kind).
 */
function serializeWithInfo<T>(
  value: T,
  spec: JsonTypeInfo<T>,
  converters?: readonly JsonConverter[],
): unknown {
  if (isValueSpec(spec)) {
    return serializeWithValueSpec(value, spec, converters);
  }

  return serializeWithSpec(value, spec, converters);
}

function deserializeWithInfo<T>(
  data: unknown,
  spec: JsonTypeInfo<T>,
  converters?: readonly JsonConverter[],
  treeNumbers?: TreeNumbers,
): T {
  if (isValueSpec(spec)) {
    return deserializeWithValueSpec(data, spec, converters, treeNumbers) as T;
  }

  return deserializeWithSpec(data, spec, converters, treeNumbers);
}

/**
 * Reads JSON text as System.Text.Json's `Deserialize(string)` does (#457).
 * The path is chosen per spec, once: a spec whose closure reads no number
 * from its exact digits (no `decimal`) reads `JSON.parse(text)`, as before.
 * One that does reads the exact tree (`readJsonText`), so each `decimal` is
 * built from its source digits, unless no number of the text can have more
 * than 15 significant digits (`mayHoldInexactNumber`), when `JSON.parse` is
 * already exact. Invalid text throws STJ's `JsonException` on every path.
 */
function deserializeTextWithInfo<T>(
  text: string,
  spec: JsonTypeInfo<T>,
  converters?: readonly JsonConverter[],
): T {
  const readsExactTree = specNeedsExactNumbers(spec) && mayHoldInexactNumber(text);

  if (readsExactTree) {
    return deserializeWithInfo(readJsonText(text), spec, converters, "exact");
  }

  return deserializeWithInfo(parseJsonText(text), spec, converters);
}

/**
 * A bound serializer that uses a SerializerContext to resolve custom converters
 * and look up specs by constructor.
 */
export class BoundSerializer {
  constructor(private readonly context: SerializerContext) {}

  /** Serialize a TS object to a JSON-safe plain object. */
  serialize<T>(value: T, spec: TypeSpec<T>): Record<string, unknown>;
  /** Serialize a non-object root (collection, map, primitive, …) to a JSON-safe value. */
  serialize<T>(value: T, spec: ValueSpec<T>): unknown;
  serialize<T>(value: T, spec: JsonTypeInfo<T>): unknown {
    return serializeWithInfo(value, spec, this.context.converters);
  }

  /**
   * Deserialize a parsed JSON value into a typed instance. A number beyond
   * double precision has already lost its digits in `JSON.parse`; read the
   * text with `deserializeText` to keep them.
   */
  deserialize<T>(data: unknown, spec: TypeSpec<T>): T;
  /** Deserialize a parsed JSON value into a non-object root value. */
  deserialize<T>(data: unknown, spec: ValueSpec<T>): T;
  deserialize<T>(data: unknown, spec: JsonTypeInfo<T>): T {
    return deserializeWithInfo(data, spec, this.context.converters);
  }

  /** Deserialize JSON text into a typed instance, decimals with their exact digits. */
  deserializeText<T>(text: string, spec: TypeSpec<T>): T;
  /** Deserialize JSON text into a non-object root value, decimals with their exact digits. */
  deserializeText<T>(text: string, spec: ValueSpec<T>): T;
  deserializeText<T>(text: string, spec: JsonTypeInfo<T>): T {
    return deserializeTextWithInfo(text, spec, this.context.converters);
  }

  /** Resolve a spec by constructor — throws if not registered. */
  specFor<T>(type: abstract new (...args: any[]) => T): TypeSpec<T> {
    const spec = this.context.resolve(type);
    if (!spec) {
      throw new Error(
        `No TypeSpec registered for ${type.name}. ` +
          `Make sure the type is listed in [JsonSerializable] on the context.`,
      );
    }
    return spec;
  }
}

/**
 * Static serializer — mirrors the C# `JsonSerializer` API.
 *
 * Usage:
 * ```ts
 * // Direct with spec
 * const json = JsonSerializer.serialize(order, JsonContext.default.order);
 * const order = JsonSerializer.deserializeText(text, JsonContext.default.order);
 * const fromTree = JsonSerializer.deserialize(JSON.parse(text), JsonContext.default.order);
 *
 * // With context (for custom converters + spec lookup)
 * const s = JsonSerializer.withContext(JsonContext.default);
 * const json = s.serialize(order, s.specFor(Order));
 * ```
 */
export class JsonSerializer {
  private constructor() {}

  /** Serialize a TS object to a JSON-safe plain object using the given spec. */
  static serialize<T>(
    value: T,
    spec: TypeSpec<T>,
    converters?: readonly JsonConverter[],
  ): Record<string, unknown>;
  /** Serialize a non-object root (collection, map, primitive, …) to a JSON-safe value. */
  static serialize<T>(value: T, spec: ValueSpec<T>, converters?: readonly JsonConverter[]): unknown;
  static serialize<T>(
    value: T,
    spec: JsonTypeInfo<T>,
    converters?: readonly JsonConverter[],
  ): unknown {
    return serializeWithInfo(value, spec, converters);
  }

  /**
   * Deserialize a parsed JSON value (`JSON.parse`'s tree) into a typed
   * instance using the given spec. `JSON.parse` has already rounded every
   * number to a double, so a `decimal` with more than 15 significant digits
   * is inexact; `deserializeText` keeps them.
   */
  static deserialize<T>(data: unknown, spec: TypeSpec<T>, converters?: readonly JsonConverter[]): T;
  /** Deserialize a parsed JSON value into a non-object root value using the given spec. */
  static deserialize<T>(
    data: unknown,
    spec: ValueSpec<T>,
    converters?: readonly JsonConverter[],
  ): T;
  static deserialize<T>(
    data: unknown,
    spec: JsonTypeInfo<T>,
    converters?: readonly JsonConverter[],
  ): T {
    return deserializeWithInfo(data, spec, converters);
  }

  /**
   * Deserialize JSON text into a typed instance using the given spec, as
   * System.Text.Json's `Deserialize(string, JsonTypeInfo<T>)` does: each
   * `decimal` is read from its exact digits, and invalid text throws
   * `JsonException` with STJ's message.
   */
  static deserializeText<T>(
    text: string,
    spec: TypeSpec<T>,
    converters?: readonly JsonConverter[],
  ): T;
  /** Deserialize JSON text into a non-object root value using the given spec. */
  static deserializeText<T>(
    text: string,
    spec: ValueSpec<T>,
    converters?: readonly JsonConverter[],
  ): T;
  static deserializeText<T>(
    text: string,
    spec: JsonTypeInfo<T>,
    converters?: readonly JsonConverter[],
  ): T {
    return deserializeTextWithInfo(text, spec, converters);
  }

  /** Create a bound serializer that uses the context's converters and registry. */
  static withContext(context: SerializerContext): BoundSerializer {
    return new BoundSerializer(context);
  }
}
