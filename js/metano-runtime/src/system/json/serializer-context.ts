import type { TypeSpec, SerializerContextOptions, JsonConverter } from "./types.ts";

/**
 * Base class for generated serializer contexts. Each context holds options
 * (including custom converters) and a registry of type specs for lookup.
 *
 * Generated subclasses expose lazy getters per type (e.g., `get order()`).
 */
export abstract class SerializerContext {
  readonly options: SerializerContextOptions;
  private readonly _registry = new Map<
    abstract new (...args: any[]) => unknown,
    TypeSpec
  >();

  constructor(options?: SerializerContextOptions) {
    this.options = options ?? {};
  }

  /** The custom converters from the context options, if any. */
  get converters(): readonly JsonConverter[] | undefined {
    return this.options.converters;
  }

  /**
   * Registers a spec in the internal registry and returns it. Intended to
   * be called from lazy getters in generated subclasses:
   *
   * ```ts
   * get order(): TypeSpec<Order> {
   *     return this._order ??= this.createSpec({ type: Order, ... });
   * }
   * ```
   */
  protected createSpec<T>(spec: TypeSpec<T>): TypeSpec<T> {
    this._registry.set(spec.type, spec as TypeSpec);
    return spec;
  }

  /**
   * Looks up a spec by constructor reference. Returns undefined if the type
   * was not registered via createSpec().
   */
  resolve<T>(type: abstract new (...args: any[]) => T): TypeSpec<T> | undefined {
    return this._registry.get(type) as TypeSpec<T> | undefined;
  }
}
