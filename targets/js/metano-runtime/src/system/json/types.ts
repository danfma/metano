// ─── Type Descriptors ────────────────────────────────────────────────────────

/**
 * The CLR name of a C# numeric type, as System.Text.Json names it in
 * conversion errors. The 64-bit integers (`System.Int64`, `System.UInt64`)
 * map to `bigint` and carry an `int64` descriptor (spec 012 FR-050); the
 * rest map to a TS `number` and carry this name as a `primitive` descriptor's
 * `numberType`.
 */
export type NumberTypeName =
  | "System.Byte"
  | "System.SByte"
  | "System.Int16"
  | "System.UInt16"
  | "System.Int32"
  | "System.UInt32"
  | "System.Int64"
  | "System.UInt64"
  | "System.Single"
  | "System.Double";

/** The CLR names of the integer types among `NumberTypeName`. */
export type IntegerTypeName = Exclude<NumberTypeName, "System.Single" | "System.Double">;

/**
 * The CLR name a descriptor carries (`typeName`) so errors report it as
 * System.Text.Json names the type: a dictionary key's rejected property name
 * (#479: `System.Boolean`, `System.Guid`, `System.DateOnly`,
 * `MyApp.Level`) and, on a temporal descriptor, the reader the runtime
 * applies (#485: `System.TimeOnly` … `System.TimeSpan`). Absent where the
 * runtime needs no type: a `string` primitive, containers, and hand-written
 * specs.
 */
type WithClrTypeName = {
  readonly typeName?: string;
};

/**
 * Primitives that JSON.stringify handles natively (string, number, boolean).
 * `numberType` names the C# numeric type when the context reads numbers from
 * JSON strings (FR-048), and always on a dictionary key, whose JSON property
 * name is parsed into the number (#479); it is absent otherwise. A
 * `System.Boolean` key names its `typeName`.
 */
export type PrimitiveDescriptor = WithClrTypeName & {
  readonly kind: "primitive";
  readonly numberType?: NumberTypeName;
};

/**
 * `long` / `ulong` — exact digits on write, integer tokens on read, as a TS
 * `bigint` (spec 012 FR-050). The `numberType` names the 64-bit type in the
 * conversion errors and decides the signedness of the integer grammars.
 * A `[Branded]` type over a 64-bit integer adds its companion `create`
 * factory (the valid `[JsonFactory]` member when one exists) and `typeName`,
 * which the read applies after the exact digit, as the branded descriptor
 * does — the factory case FR-050 does not describe.
 */
export type Int64Descriptor = {
  readonly kind: "int64";
  readonly numberType: "System.Int64" | "System.UInt64";
  readonly create?: (value: any) => unknown;
  readonly typeName?: string;
};

/** Another spec — recursive serialize/deserialize */
export type RefDescriptor<T = unknown> = {
  readonly kind: "ref";
  readonly spec: () => TypeSpec<T>;
};

/**
 * Temporal types — `toString()` to serialize (`Temporal.PlainTime` in the
 * STJ `TimeOnly` form, `temporal-text.ts`), `parse()` to deserialize.
 */
export type TemporalDescriptor = WithClrTypeName & {
  readonly kind: "temporal";
  /**
   * The parse of a hand-written descriptor (`Temporal.PlainDate.from`, …).
   * Generated descriptors name their `typeName` instead, so the runtime
   * reads with System.Text.Json's own grammar (#485).
   */
  readonly parse?: (iso: string) => unknown;
};

/**
 * `System.Decimal` — its exact digits on serialize (a `JSON.rawJSON` leaf of
 * `toFixed()`, #458), overridable via a custom converter. `type` builds the
 * value on deserialize from the JSON number or, when the context reads
 * numbers from strings, the JSON text (decimal.js `Decimal` in generated
 * contexts); without it the JSON value is kept as is.
 */
export type DecimalDescriptor = WithClrTypeName & {
  readonly kind: "decimal";
  readonly type?: new (value: string | number) => unknown;
};

/**
 * `byte[]` — a base64 JSON string, as System.Text.Json writes and reads it
 * (FR-046, slice S6b). The TS value is the `number[]` a C# `byte[]` maps to.
 */
export type BytesDescriptor = {
  readonly kind: "bytes";
};

/**
 * Map<K,V> — Object.fromEntries on serialize, new Map(Object.entries) on
 * deserialize. Each JSON property name is parsed into the `key` type as
 * System.Text.Json parses it (`dictionary-keys.ts`, #479).
 */
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
export type BrandedDescriptor = WithClrTypeName & {
  readonly kind: "branded";
  readonly create: (value: any) => unknown;
};

/** String enum — passthrough on serialize, validate on deserialize */
export type EnumDescriptor = WithClrTypeName & {
  readonly kind: "enum";
  readonly values: Record<string, string>;
};

/**
 * Numeric enum — passthrough both ways. `values` is the TS enum object itself,
 * whose reverse mapping (`Level[0] === "Low"`) adds string values; only the
 * numbers are valid members.
 */
export type NumericEnumDescriptor = WithClrTypeName & {
  readonly kind: "numericEnum";
  /**
   * The enum's underlying integer type, set on a dictionary key: an integer
   * key name is range-checked by it (#479). `System.Int32` when absent.
   */
  readonly numberType?: IntegerTypeName;
  readonly values: Record<string, string | number>;
};

/** Nullable wrapper — delegates to inner descriptor, passes null through */
export type NullableDescriptor = {
  readonly kind: "nullable";
  readonly inner: TypeDescriptor;
};

export type TypeDescriptor =
  | PrimitiveDescriptor
  | Int64Descriptor
  | RefDescriptor
  | TemporalDescriptor
  | DecimalDescriptor
  | BytesDescriptor
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
  /**
   * `[JsonPropertyOrder]`: the wire order sorts every property of the
   * contract by it, stably, absent counting as `0` (FR-046, slice S6b).
   */
  readonly order?: number;
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
   * properties of the ancestors follow, nearest first (FR-009). A property's
   * `order` then sorts the whole chain (`collectWireProperties`).
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
  /**
   * A C# `struct` contract: JSON `null` at the root throws `JsonException`
   * instead of reading as `null` (FR-037), as System.Text.Json does for a
   * value type.
   */
  readonly valueType?: boolean;
  /**
   * The CLR name System.Text.Json prints for this struct contract
   * (`MyApp.SPoint`, `MyApp.Outer+Inner`, ``MyApp.Pair`1[System.Int32]``);
   * the generator emits it next to `valueType`. Errors name the constructor
   * without it.
   */
  readonly typeName?: string;
  /** The context's read behavior; absent when it is System.Text.Json's default. */
  readonly readOptions?: JsonReadOptions;
  /**
   * TS names of this contract's properties (its base chain's included) that
   * a read sets after construction, and only when the payload carries their
   * key: a missing key keeps the value the factory constructed (a C#
   * initializer or a constructor's assignment), as System.Text.Json does for
   * a `set` accessor or a writable field that no parameter of this
   * contract's constructor binds. The generated factory ignores them; the
   * runtime assigns them after construction (FR-046). Each contract has its
   * own list: a derived spec's constructor binds different members.
   */
  readonly assignIfPresent?: readonly string[];
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
  /** JSON keys match property names ignoring case; the last match wins (FR-048). */
  readonly propertyNameCaseInsensitive?: boolean;
  /** Numeric values may also arrive as JSON strings (FR-048). Writes are unaffected. */
  readonly numberHandling?: "allowReadingFromString";
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
  /**
   * A C# value type (`int`, `Guid`, an enum, a branded type, …): JSON `null`
   * at the root throws `JsonException` (FR-037). Absent for a reference-type
   * root, whose `T` already holds `| null`, and for `Nullable<T>`.
   */
  readonly valueType?: boolean;
  /**
   * The CLR name System.Text.Json prints for this value type
   * (`System.Int32`, `MyApp.Level`); the generator emits it next to
   * `valueType`. Errors name `Object` without it.
   */
  readonly typeName?: string;
  /** The context's read behavior; absent when it is System.Text.Json's default. */
  readonly readOptions?: JsonReadOptions;
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
