import type { TypeSpec, ValueSpec, JsonTypeInfo, JsonConverter } from "./types.ts";
import type { SerializerContext } from "./serializer-context.ts";
import { serializeWithSpec, deserializeWithSpec, resolveConverter } from "./converters.ts";

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
    return resolveConverter(spec.descriptor, converters).serialize(value);
  }

  return serializeWithSpec(value, spec, converters);
}

function deserializeWithInfo<T>(
  data: unknown,
  spec: JsonTypeInfo<T>,
  converters?: readonly JsonConverter[],
): T {
  if (isValueSpec(spec)) {
    return resolveConverter(spec.descriptor, converters).deserialize(data) as T;
  }

  return deserializeWithSpec(data, spec, converters);
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

  /** Deserialize unknown data into a typed instance. */
  deserialize<T>(data: unknown, spec: TypeSpec<T>): T;
  /** Deserialize unknown data into a non-object root value. */
  deserialize<T>(data: unknown, spec: ValueSpec<T>): T;
  deserialize<T>(data: unknown, spec: JsonTypeInfo<T>): T {
    return deserializeWithInfo(data, spec, this.context.converters);
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
 * const order = JsonSerializer.deserialize(data, JsonContext.default.order);
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

  /** Deserialize unknown data into a typed instance using the given spec. */
  static deserialize<T>(data: unknown, spec: TypeSpec<T>, converters?: readonly JsonConverter[]): T;
  /** Deserialize unknown data into a non-object root value using the given spec. */
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

  /** Create a bound serializer that uses the context's converters and registry. */
  static withContext(context: SerializerContext): BoundSerializer {
    return new BoundSerializer(context);
  }
}
