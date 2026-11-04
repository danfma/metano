// ─── Type Descriptors ────────────────────────────────────────────────────────

/** Primitives that JSON.stringify handles natively (string, number, boolean) */
export type PrimitiveDescriptor = { readonly kind: "primitive" };

/** Another spec — recursive serialize/deserialize */
export type RefDescriptor<T = unknown> = {
  readonly kind: "ref";
  readonly spec: () => TypeSpec<T>;
};

/** Temporal types — toString() to serialize, parse() to deserialize */
export type TemporalDescriptor = {
  readonly kind: "temporal";
  readonly parse: (iso: string) => unknown;
};

/** decimal.js Decimal — toNumber() by default, overridable via custom converter */
export type DecimalDescriptor = { readonly kind: "decimal" };

/** Map<K,V> — Object.fromEntries on serialize, new Map(Object.entries) on deserialize */
export type MapDescriptor = {
  readonly kind: "map";
  readonly key: TypeDescriptor;
  readonly value: TypeDescriptor;
};

/** Array<T> */
export type ArrayDescriptor = {
  readonly kind: "array";
  readonly element: TypeDescriptor;
};

/** HashSet<T> — spread to array on serialize, reconstruct on deserialize */
export type HashSetDescriptor = {
  readonly kind: "hashSet";
  readonly element: TypeDescriptor;
};

/**
 * Branded type ([InlineWrapper]) — passthrough on serialize, create() on
 * deserialize.
 *
 * The `create` parameter is typed `any` by design: the descriptor is a
 * serializer-internal lookup table, and the runtime calls `create()` with
 * whatever JSON-parsed primitive matched the wire format (a string for most
 * branded IDs, a number for numeric wrappers, etc.). Narrowing to `unknown`
 * would refuse to accept the generated `UserId.create: (value: string) => UserId`
 * under TypeScript's contravariant parameter check, and the fix would be to
 * widen every `[InlineWrapper]`'s generated `.create()` to accept `unknown`
 * and re-validate — which pollutes the branded type's user-facing surface
 * for no real benefit, since consumers always call `UserId.create("abc")`
 * with a typed string, never with raw parsed JSON.
 */
export type BrandedDescriptor = {
  readonly kind: "branded";
  readonly create: (value: any) => unknown;
};

/** String enum — passthrough on serialize, validate on deserialize */
export type EnumDescriptor = {
  readonly kind: "enum";
  readonly values: Record<string, string>;
};

/**
 * Numeric enum — passthrough both ways. `values` is the TS enum object itself,
 * whose reverse mapping (`Level[0] === "Low"`) adds string values; only the
 * numbers are valid members.
 */
export type NumericEnumDescriptor = {
  readonly kind: "numericEnum";
  readonly values: Record<string, string | number>;
};

/** Nullable wrapper — delegates to inner descriptor, passes null through */
export type NullableDescriptor = {
  readonly kind: "nullable";
  readonly inner: TypeDescriptor;
};

export type TypeDescriptor =
  | PrimitiveDescriptor
  | RefDescriptor
  | TemporalDescriptor
  | DecimalDescriptor
  | MapDescriptor
  | ArrayDescriptor
  | HashSetDescriptor
  | BrandedDescriptor
  | EnumDescriptor
  | NumericEnumDescriptor
  | NullableDescriptor;

// ─── Property Spec ───────────────────────────────────────────────────────────

export interface PropertySpec {
  /** TypeScript field name (camelCase) */
  readonly ts: string;
  /** JSON wire name (pre-computed by transpiler) */
  readonly json: string;
  /** How to convert between TS and JSON */
  readonly type: TypeDescriptor;
  /** Property is optional — null is accepted on deserialize */
  readonly optional?: boolean;
}

// ─── Type Spec ───────────────────────────────────────────────────────────────

export interface TypeSpec<T = unknown> {
  /**
   * Constructor reference (for instanceof checks and context lookups).
   * Optional for [PlainObject] types that lower to TypeScript interfaces —
   * they have no runtime constructor, so the spec is accessed via its getter
   * on the context (e.g., `ctx.storedTodo`) rather than via `resolve(Class)`.
   */
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  readonly type?: abstract new (
    ...args: any[]
  ) => T;
  /** Parent type's spec — properties are inherited */
  readonly base?: TypeSpec;
  /** Constructs T from a Record<ts-field-name, deserialized-value> */
  readonly factory: (props: Record<string, unknown>) => T;
  /**
   * Own properties, in wire order (base properties come from the base spec).
   * For a spec without `base` whose C# base has no spec, the inlined
   * properties of the ancestors follow, nearest first (FR-009).
   */
  readonly properties: readonly PropertySpec[];
  /**
   * TS names of base properties this contract hides with a `[JsonIgnore]`
   * override: no spec of its base chain writes or reads them for this
   * contract (System.Text.Json writes none of them).
   */
  readonly ignoredProperties?: readonly string[];
  /**
   * Present on a polymorphic base (`[JsonDerivedType]`): serializing and
   * deserializing through this spec dispatch to a derived contract. A
   * derived contract reached by dispatch is never dispatched again.
   */
  readonly polymorphism?: PolymorphismSpec;
  /** The context's read behavior; absent when it is System.Text.Json's default. */
  readonly readOptions?: JsonReadOptions;
}

// ─── Polymorphism ────────────────────────────────────────────────────────────

/** A `[JsonDerivedType]` tag: a JSON string or a JSON number, never equal to each other. */
export type TypeDiscriminator = string | number;

/** `JsonUnknownDerivedTypeHandling`: what a write does with an unregistered runtime type. */
export type UnknownDerivedTypeHandling =
  | "failSerialization"
  | "fallBackToBaseType"
  | "fallBackToNearestAncestor";

/** One `[JsonDerivedType]` registration of a polymorphic base (possibly the base itself). */
export interface DerivedTypeSpec {
  /** Absent for an untagged registration. */
  readonly tag?: TypeDiscriminator;
  /** Lazy: base and derived getters reference each other. */
  readonly spec: () => TypeSpec;
  /** `[PlainObject]` variants only: the guard that selects this variant on write. */
  readonly test?: (value: unknown) => boolean;
  /**
   * The registered type is abstract, which `fallBackToNearestAncestor` alone
   * allows (FR-017): it is a nearest-ancestor target on write, and reading its
   * tag throws `NotSupportedException` (DECIDED-12).
   */
  readonly abstract?: boolean;
}

/** The data-only `[JsonPolymorphic]` / `[JsonDerivedType]` description of a base. */
export interface PolymorphismSpec {
  /** The discriminator property name, verbatim (never passed through a naming policy). */
  readonly discriminator: string;
  /** The C# base is abstract or an interface: a missing or ignored tag cannot read the base. */
  readonly abstract?: boolean;
  /** The C# simple name for messages, when the base has no constructor (interface, `[PlainObject]`). */
  readonly typeName?: string;
  /** The registrations, in attribute order. */
  readonly derived: readonly DerivedTypeSpec[];
  /** Indices into `derived` in guard test order; absent when it is attribute order. */
  readonly testOrder?: readonly number[];
  /**
   * The guard of a concrete `[PlainObject]` base that does not register itself: a plain object
   * no variant guard accepts is written as the base (no tag) when this accepts it (FR-019 rule 5).
   */
  readonly baseTest?: (value: unknown) => boolean;
  /** Default `"failSerialization"`. */
  readonly unknownDerivedType?: UnknownDerivedTypeHandling;
  /** An unknown tag reads the base (a concrete base) instead of throwing. */
  readonly ignoreUnrecognizedTags?: boolean;
}

/**
 * Context-level read behavior mirrored from `[JsonSourceGenerationOptions]`.
 * Every member is optional; an absent member is System.Text.Json's default.
 */
export interface JsonReadOptions {
  /** The type discriminator may appear after other properties (FR-002). */
  readonly allowOutOfOrderMetadataProperties?: boolean;
}

// ─── Value Spec ──────────────────────────────────────────────────────────────

/**
 * Spec of a non-object `[JsonSerializable]` root (a collection, dictionary,
 * set, primitive, enum, branded or nullable type): the same descriptor that
 * type gets as a property. `T` is the root's TS type and only types the
 * `JsonSerializer` overloads; it has no runtime representation.
 */
export interface ValueSpec<T = unknown> {
  /** How to convert the whole value between TS and JSON */
  readonly descriptor: TypeDescriptor;
  /**
   * Phantom member that keeps `T` in the type; never set at runtime. Covariant
   * like `TypeSpec<T>`, so any `ValueSpec<T>` is a `ValueSpec<unknown>`.
   */
  readonly __type?: () => T;
}

/** Any spec the generated context exposes: an object contract or a value root. */
export type JsonTypeInfo<T = unknown> = TypeSpec<T> | ValueSpec<T>;

// ─── Converter ───────────────────────────────────────────────────────────────

/** Custom converter — overrides built-in handling for a type descriptor kind */
export interface JsonConverter {
  readonly kind: TypeDescriptor["kind"];
  readonly serialize: (value: unknown) => unknown;
  readonly deserialize: (value: unknown) => unknown;
}

// ─── Context Options ─────────────────────────────────────────────────────────

export interface SerializerContextOptions {
  /** Custom converters that override built-in type handling */
  readonly converters?: readonly JsonConverter[];
}
