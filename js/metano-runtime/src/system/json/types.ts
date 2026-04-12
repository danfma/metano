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
  // biome-ignore lint/suspicious/noExplicitAny: see doc comment above
  readonly create: (value: any) => unknown;
};

/** String enum — passthrough on serialize, validate on deserialize */
export type EnumDescriptor = {
  readonly kind: "enum";
  readonly values: Record<string, string>;
};

/** Numeric enum — passthrough both ways */
export type NumericEnumDescriptor = {
  readonly kind: "numericEnum";
  readonly values: Record<string, number>;
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
  /** Own properties only (base properties come from base spec) */
  readonly properties: readonly PropertySpec[];
}

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
