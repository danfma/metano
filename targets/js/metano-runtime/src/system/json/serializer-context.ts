import type {
  TypeSpec,
  ValueSpec,
  SerializerContextOptions,
  JsonConverter,
  JsonReadOptions,
} from "./types.ts";

/**
 * Base class for generated serializer contexts. Each context holds options
 * (including custom converters) and a registry of type specs for lookup.
 *
 * Generated subclasses expose lazy getters per type (e.g., `get order()`).
 */
export abstract class SerializerContext {
  readonly options: SerializerContextOptions;
  private readonly _registry = new Map<abstract new (...args: any[]) => unknown, TypeSpec>();

  constructor(options?: SerializerContextOptions) {
    this.options = options ?? {};
  }

  /** The custom converters from the context options, if any. */
  get converters(): readonly JsonConverter[] | undefined {
    return this.options.converters;
  }

  /**
   * The read behavior of every spec this context creates, mirrored from the
   * C# context's `[JsonSourceGenerationOptions]` (FR-002, FR-048). Generated
   * contexts override it only when an option differs from System.Text.Json's
   * default.
   */
  protected get readOptions(): JsonReadOptions {
    return {};
  }

  /**
   * Registers a spec in the internal registry and returns it, with the
   * context's read options attached. Intended to be called from lazy getters
   * in generated subclasses:
   *
   * ```ts
   * get order(): TypeSpec<Order> {
   *     return this._order ??= this.createSpec({ type: Order, ... });
   * }
   * ```
   */
  protected createSpec<T>(spec: TypeSpec<T>): TypeSpec<T> {
    const attached = this.withReadOptions(spec);

    // Only register class-backed types (those with a constructor reference).
    // [PlainObject] specs have no `type` field — they're accessed via their
    // getter on the context, not via constructor-keyed resolve().
    if (attached.type) {
      this._registry.set(attached.type, attached as TypeSpec);
    }

    return attached;
  }

  /**
   * Returns the spec of a non-object root (a collection, dictionary, set,
   * primitive, enum, branded or nullable type). Intended to be called from
   * lazy getters in generated subclasses:
   *
   * ```ts
   * get listEpisode(): ValueSpec<Episode[] | null> {
   *     return this._listEpisode ??= this.createValueSpec({ descriptor: { kind: "array", ... } });
   * }
   * ```
   *
   * Nothing is registered: a value spec has no constructor to resolve. The
   * context's read options are attached, as by `createSpec`.
   */
  protected createValueSpec<T>(spec: ValueSpec<T>): ValueSpec<T> {
    return this.withReadOptions(spec);
  }

  /**
   * `spec` with the context's read options, under any the spec declares
   * itself. A spec stays as given when neither has one, so default contexts
   * keep their specs unchanged.
   */
  private withReadOptions<S extends { readonly readOptions?: JsonReadOptions }>(spec: S): S {
    const readOptions = { ...this.readOptions, ...spec.readOptions };

    return Object.keys(readOptions).length === 0 ? spec : { ...spec, readOptions };
  }

  /**
   * Looks up a spec by constructor reference. Returns undefined if the type
   * was not registered via createSpec().
   */
  resolve<T>(type: abstract new (...args: any[]) => T): TypeSpec<T> | undefined {
    return this._registry.get(type) as TypeSpec<T> | undefined;
  }
}
