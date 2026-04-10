import type { TypeSpec, JsonConverter } from "./types.ts";
import type { SerializerContext } from "./serializer-context.ts";
import { serializeWithSpec, deserializeWithSpec } from "./converters.ts";

/**
 * A bound serializer that uses a SerializerContext to resolve custom converters
 * and look up specs by constructor.
 */
export class BoundSerializer {
  constructor(private readonly context: SerializerContext) {}

  /** Serialize a TS object to a JSON-safe plain object. */
  serialize<T>(value: T, spec: TypeSpec<T>): Record<string, unknown> {
    return serializeWithSpec(value, spec, this.context.converters);
  }

  /** Deserialize unknown data into a typed instance. */
  deserialize<T>(data: unknown, spec: TypeSpec<T>): T {
    return deserializeWithSpec(data, spec, this.context.converters);
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
  ): Record<string, unknown> {
    return serializeWithSpec(value, spec, converters);
  }

  /** Deserialize unknown data into a typed instance using the given spec. */
  static deserialize<T>(
    data: unknown,
    spec: TypeSpec<T>,
    converters?: readonly JsonConverter[],
  ): T {
    return deserializeWithSpec(data, spec, converters);
  }

  /** Create a bound serializer that uses the context's converters and registry. */
  static withContext(context: SerializerContext): BoundSerializer {
    return new BoundSerializer(context);
  }
}
