import type { PolymorphismSpec, TypeDiscriminator, TypeSpec } from "./types.ts";
import { cannotConvertTo, contractNameOf } from "./contract-name.ts";
import { JsonException, NotSupportedException } from "./errors.ts";

/**
 * The contract a polymorphic base writes a value with, and the tag written
 * before its properties (absent: no tag).
 */
export interface SerializeTarget {
  readonly spec: TypeSpec;
  readonly tag?: TypeDiscriminator;
}

/** The synthesized guard of a `[PlainObject]` variant. */
type VariantGuard = (value: unknown) => boolean;

/** One registration with its contract resolved. */
interface ResolvedVariant extends SerializeTarget {
  readonly test?: VariantGuard;
  readonly abstract?: boolean;
}

/** A `[PlainObject]` variant, with the guard that selects it on write. */
interface StructuralVariant extends ResolvedVariant {
  readonly test: VariantGuard;
}

/**
 * The lookup tables of one polymorphic base, built on first use (never inside
 * a generated getter, whose specs reference each other): class variants by
 * constructor, registrations by tag (a `Map`, so `1` and `"1"` stay distinct),
 * and `[PlainObject]` variants in guard test order.
 */
interface DispatchIndex {
  readonly byConstructor: ReadonlyMap<unknown, SerializeTarget>;
  readonly byTag: ReadonlyMap<TypeDiscriminator, ResolvedVariant>;
  readonly structural: readonly StructuralVariant[];
}

const dispatchIndexes = new WeakMap<PolymorphismSpec, DispatchIndex>();

function dispatchIndexOf(polymorphism: PolymorphismSpec): DispatchIndex {
  const cached = dispatchIndexes.get(polymorphism);

  if (cached) {
    return cached;
  }

  const index = buildDispatchIndex(polymorphism);
  dispatchIndexes.set(polymorphism, index);

  return index;
}

function buildDispatchIndex(polymorphism: PolymorphismSpec): DispatchIndex {
  const variants = polymorphism.derived.map<ResolvedVariant>((entry) => ({
    tag: entry.tag,
    spec: entry.spec(),
    test: entry.test,
    abstract: entry.abstract,
  }));

  const byConstructor = new Map<unknown, SerializeTarget>();
  const byTag = new Map<TypeDiscriminator, ResolvedVariant>();

  for (const variant of variants) {
    if (variant.spec.type) {
      byConstructor.set(variant.spec.type, { spec: variant.spec, tag: variant.tag });
    }

    if (variant.tag !== undefined) {
      byTag.set(variant.tag, variant);
    }
  }

  const testOrder = polymorphism.testOrder ?? variants.map((_, position) => position);

  const structural = testOrder
    .map((position) => variants[position])
    .filter((variant): variant is StructuralVariant => variant?.test !== undefined);

  return { byConstructor, byTag, structural };
}

// ─── Serialize ───────────────────────────────────────────────────────────────

/**
 * Chooses the contract and tag a polymorphic base writes `value` with
 * (FR-005, FR-019): a class instance by its constructor (a registered type
 * first, then the base itself), a plain object by the `[PlainObject]` variant
 * guards in test order, anything else by `unknownDerivedType`.
 */
export function resolveSerializeTarget(
  value: unknown,
  spec: TypeSpec,
  polymorphism: PolymorphismSpec,
): SerializeTarget {
  const index = dispatchIndexOf(polymorphism);

  const target = isPlainObject(value)
    ? matchStructuralVariant(value, spec, polymorphism, index)
    : matchConstructor(constructorOf(value), spec, index);

  return target ?? unknownDerivedTypeTarget(value, spec, polymorphism, index);
}

/** A value whose prototype is `Object.prototype` or `null`: it has no runtime type. */
function isPlainObject(value: unknown): value is object {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const prototype = Object.getPrototypeOf(value);

  return prototype === Object.prototype || prototype === null;
}

function constructorOf(value: unknown): unknown {
  return Object.getPrototypeOf(value).constructor;
}

function matchConstructor(
  runtimeType: unknown,
  spec: TypeSpec,
  index: DispatchIndex,
): SerializeTarget | undefined {
  const registered = index.byConstructor.get(runtimeType);

  if (registered) {
    return registered;
  }

  return runtimeType === spec.type ? { spec } : undefined;
}

/**
 * The first `[PlainObject]` variant whose guard accepts `value`. With none,
 * a concrete base without a constructor (a `[PlainObject]` base) writes the
 * value as the base itself, with no tag.
 */
function matchStructuralVariant(
  value: object,
  spec: TypeSpec,
  polymorphism: PolymorphismSpec,
  index: DispatchIndex,
): SerializeTarget | undefined {
  const variant = index.structural.find((candidate) => candidate.test(value));

  if (variant) {
    return variant;
  }

  const isConcretePlainObjectBase = !polymorphism.abstract && spec.type === undefined;

  return isConcretePlainObjectBase ? { spec } : undefined;
}

/**
 * The target of a runtime type the base does not register (FR-005), per
 * `unknownDerivedType`: fail, the base contract with no tag, or the nearest
 * registered ancestor (else the base contract).
 */
function unknownDerivedTypeTarget(
  value: unknown,
  spec: TypeSpec,
  polymorphism: PolymorphismSpec,
  index: DispatchIndex,
): SerializeTarget {
  switch (polymorphism.unknownDerivedType ?? "failSerialization") {
    case "failSerialization":
      throw new NotSupportedException(
        `Runtime type '${runtimeTypeNameOf(value)}' is not supported by polymorphic type ` +
          `'${contractNameOf(spec)}'.`,
      );

    case "fallBackToBaseType":
      return { spec };

    case "fallBackToNearestAncestor":
      return nearestRegisteredAncestorOf(value, index) ?? { spec };
  }
}

/**
 * The registration of the nearest ancestor class of `value`'s constructor,
 * abstract registrations included (FR-017). A plain object has none: its
 * most specific matching guard already is its nearest ancestor (FR-019).
 */
function nearestRegisteredAncestorOf(
  value: unknown,
  index: DispatchIndex,
): SerializeTarget | undefined {
  if (isPlainObject(value)) {
    return undefined;
  }

  for (const ancestor of ancestorConstructorsOf(constructorOf(value))) {
    const registered = index.byConstructor.get(ancestor);

    if (registered) {
      return registered;
    }
  }

  return undefined;
}

/** The constructors `runtimeType` extends, nearest first. */
function* ancestorConstructorsOf(runtimeType: unknown): Generator<unknown> {
  const parent: unknown = Object.getPrototypeOf(runtimeType);

  if (parent === null || parent === Function.prototype) {
    return;
  }

  yield parent;
  yield* ancestorConstructorsOf(parent);
}

/** The runtime type name STJ would report: the constructor's name, `Object` for a plain object. */
function runtimeTypeNameOf(value: unknown): string {
  if (isPlainObject(value)) {
    return "Object";
  }

  const runtimeType = constructorOf(value) as { readonly name?: string } | undefined;

  return runtimeType?.name || "Object";
}

// ─── Deserialize ─────────────────────────────────────────────────────────────

/** The prefix System.Text.Json reserves for metadata property names. */
const METADATA_PREFIX = "$";

/** The metadata property of a preserved array. */
const VALUES_KEY = "$values";

/** Metadata names System.Text.Json knows besides the discriminator, unsupported here. */
const REFERENCE_METADATA_NAMES: ReadonlySet<string> = new Set(["$id", "$ref"]);

/** A metadata property that is unsupported, or not the first key of the payload. */
const NOT_FIRST_METADATA_MESSAGE =
  "The metadata property is either not supported by the type or is not the first property in " +
  "the deserialized JSON object.";

/** Any other key that starts with `$` in a polymorphic payload. */
const DOLLAR_PROPERTY_MESSAGE =
  "Properties that start with '$' are not allowed in types that support metadata. Either " +
  "replace the character or disable reference preservation and polymorphic deserialization.";

/** `$values` as the first key of a polymorphic payload. */
const LEADING_VALUES_MESSAGE =
  "A '$values' metadata property must always be preceded by other metadata properties, such " +
  "as '$id' or '$type'.";

const ARRAY_INDEX_PATTERN = /^(?:0|[1-9]\d*)$/;

const MAX_ARRAY_INDEX = 4_294_967_294;

const INT32_MIN = -2_147_483_648;

const INT32_MAX = 2_147_483_647;

/**
 * Chooses the contract a polymorphic base reads `raw` with (FR-007): the
 * contract of a known tag, the base for a missing tag (or an ignored unknown
 * one) of a concrete base. Throws System.Text.Json's exceptions otherwise,
 * including for misplaced metadata: a later discriminator, `$id` / `$ref` /
 * `$values`, or any other `$`-prefixed key.
 */
export function resolveDeserializeTarget(
  raw: Record<string, unknown>,
  spec: TypeSpec,
  polymorphism: PolymorphismSpec,
): TypeSpec {
  const keys = documentOrderKeysOf(raw);
  const allowsOutOfOrderTag = spec.readOptions?.allowOutOfOrderMetadataProperties ?? false;

  return allowsOutOfOrderTag
    ? resolveWithTagAnywhere(raw, keys, spec, polymorphism)
    : resolveWithLeadingTag(raw, keys, spec, polymorphism);
}

/**
 * The default: the tag counts only as the first key. Metadata in the first
 * position is checked before a missing tag is reported; every later key is
 * checked after the contract is chosen.
 */
function resolveWithLeadingTag(
  raw: Record<string, unknown>,
  keys: readonly string[],
  spec: TypeSpec,
  polymorphism: PolymorphismSpec,
): TypeSpec {
  const { discriminator } = polymorphism;
  const [firstKey] = keys;
  const hasLeadingTag = firstKey === discriminator;

  if (!hasLeadingTag && firstKey !== undefined) {
    throwIfLeadingMetadata(firstKey);
  }

  const tag = hasLeadingTag ? readTag(raw[discriminator], spec) : undefined;
  const laterKeys = hasLeadingTag ? keys.slice(1) : keys;

  if (isAbstractRegistrationTag(tag, polymorphism)) {
    throwIfMetadataFollowsTag(laterKeys[0]);
  }

  const target = contractOfTag(tag, spec, polymorphism);

  for (const key of laterKeys) {
    throwIfMetadata(key, discriminator, NOT_FIRST_METADATA_MESSAGE);
  }

  return target;
}

/** What the out-of-order walk has seen so far. */
interface OutOfOrderWalk {
  tag: TypeDiscriminator | undefined;
  sawDataKey: boolean;
  valuesPending: boolean;
}

/**
 * `allowOutOfOrderMetadataProperties`: every key is checked in document
 * order (the tag's JSON kind where it appears), and the contract is chosen
 * after the walk. A `$values` key makes System.Text.Json expect a preserved
 * array: a data key before it, or any non-metadata key after it, fails with
 * its two-reason message; an object that ends with `$values` pending fails
 * after its tag is resolved, or with the leading-`$values` message when it
 * has no tag (measured on .NET 10).
 */
function resolveWithTagAnywhere(
  raw: Record<string, unknown>,
  keys: readonly string[],
  spec: TypeSpec,
  polymorphism: PolymorphismSpec,
): TypeSpec {
  const walk: OutOfOrderWalk = { tag: undefined, sawDataKey: false, valuesPending: false };

  for (const key of keys) {
    visitOutOfOrderKey(key, raw, spec, polymorphism.discriminator, walk);
  }

  if (!walk.valuesPending) {
    return contractOfTag(walk.tag, spec, polymorphism);
  }

  if (walk.tag === undefined) {
    throw new JsonException(LEADING_VALUES_MESSAGE);
  }

  throwIfUnrecognizedTag(walk.tag, polymorphism);

  throw new JsonException(NOT_FIRST_METADATA_MESSAGE);
}

function visitOutOfOrderKey(
  key: string,
  raw: Record<string, unknown>,
  spec: TypeSpec,
  discriminator: string,
  walk: OutOfOrderWalk,
): void {
  if (key === discriminator) {
    walk.tag = readTag(raw[discriminator], spec);
    return;
  }

  if (REFERENCE_METADATA_NAMES.has(key)) {
    throw new JsonException(NOT_FIRST_METADATA_MESSAGE);
  }

  if (key === VALUES_KEY) {
    if (walk.sawDataKey) {
      throw new JsonException(outOfOrderValuesMessage(key, spec));
    }

    walk.valuesPending = true;
    return;
  }

  if (walk.valuesPending) {
    throw new JsonException(outOfOrderValuesMessage(key, spec));
  }

  throwIfMetadata(key, discriminator, NOT_FIRST_METADATA_MESSAGE);
  walk.sawDataKey = true;
}

/**
 * STJ's two-reason message when `$values` and a non-metadata `propertyName`
 * share an object read with out-of-order metadata allowed.
 */
function outOfOrderValuesMessage(propertyName: string, spec: TypeSpec): string {
  return (
    "Deserialization failed for one of these reasons:\n" +
    "1. A JSON object containing metadata for a nested array includes a non-metadata property " +
    `'${propertyName}'.\n` +
    `2. The JSON value could not be converted to ${contractNameOf(spec)}.`
  );
}

/**
 * The keys of a parsed object in JSON document order, as far as JS keeps it:
 * array-index keys (`"0"`, `"1"`, …) are enumerated first whatever their
 * position, so they are left out. They never start with `$`, so the metadata
 * checks lose nothing; only an index key written before the tag goes
 * unnoticed (a documented divergence).
 */
function documentOrderKeysOf(raw: Record<string, unknown>): readonly string[] {
  return Object.keys(raw).filter((key) => !isArrayIndex(key));
}

function isArrayIndex(key: string): boolean {
  return ARRAY_INDEX_PATTERN.test(key) && Number(key) <= MAX_ARRAY_INDEX;
}

/**
 * Whether `tag` selects an abstract registration (DECIDED-12), whose read
 * fails once the metadata that directly follows the tag has been read.
 */
function isAbstractRegistrationTag(
  tag: TypeDiscriminator | undefined,
  polymorphism: PolymorphismSpec,
): boolean {
  return tag !== undefined && dispatchIndexOf(polymorphism).byTag.get(tag)?.abstract === true;
}

/**
 * `$id` / `$ref` / `$values` right after the tag fail as misplaced metadata
 * before an abstract registration is reported (measured on .NET 10).
 */
function throwIfMetadataFollowsTag(key: string | undefined): void {
  if (key === VALUES_KEY || (key !== undefined && REFERENCE_METADATA_NAMES.has(key))) {
    throw new JsonException(NOT_FIRST_METADATA_MESSAGE);
  }
}

/** `$id` / `$ref` / `$values` as the first key fail before a missing tag is reported. */
function throwIfLeadingMetadata(key: string): void {
  if (key === VALUES_KEY) {
    throw new JsonException(LEADING_VALUES_MESSAGE);
  }

  if (REFERENCE_METADATA_NAMES.has(key)) {
    throw new JsonException(NOT_FIRST_METADATA_MESSAGE);
  }
}

/**
 * Rejects a key System.Text.Json reads as misplaced metadata: the
 * discriminator, `$id` / `$ref` (unsupported or not first), `$values` (with
 * `valuesMessage`), or any other key that starts with `$`.
 */
function throwIfMetadata(key: string, discriminator: string, valuesMessage: string): void {
  const message = metadataErrorMessageOf(key, discriminator, valuesMessage);

  if (message !== undefined) {
    throw new JsonException(message);
  }
}

function metadataErrorMessageOf(
  key: string,
  discriminator: string,
  valuesMessage: string,
): string | undefined {
  if (key === discriminator || REFERENCE_METADATA_NAMES.has(key)) {
    return NOT_FIRST_METADATA_MESSAGE;
  }

  if (key === VALUES_KEY) {
    return valuesMessage;
  }

  return key.startsWith(METADATA_PREFIX) ? DOLLAR_PROPERTY_MESSAGE : undefined;
}

/**
 * The contract a tag selects (FR-007); `undefined` is a missing tag. An
 * unknown tag reads like a missing one under `ignoreUnrecognizedTags`.
 */
function contractOfTag(
  tag: TypeDiscriminator | undefined,
  spec: TypeSpec,
  polymorphism: PolymorphismSpec,
): TypeSpec {
  if (tag === undefined) {
    return contractWithoutTag(spec, polymorphism);
  }

  const registration = dispatchIndexOf(polymorphism).byTag.get(tag);

  if (registration) {
    return contractOfRegistration(registration);
  }

  throwIfUnrecognizedTag(tag, polymorphism);

  return contractWithoutTag(spec, polymorphism);
}

/**
 * The contract a known tag reads (the base's own for a self-registration).
 * An abstract registration cannot be read (DECIDED-12, measured on .NET 10):
 * an abstract type that is itself polymorphic reports its missing tag, as
 * its own read dispatches again; any other abstract type is unsupported.
 */
function contractOfRegistration(registration: ResolvedVariant): TypeSpec {
  const { spec } = registration;

  if (!registration.abstract) {
    return spec;
  }

  if (spec.polymorphism) {
    throw missingDiscriminatorException(spec);
  }

  throw new NotSupportedException(
    "Deserialization of interface or abstract types is not supported. " +
      `Type '${contractNameOf(spec)}'.`,
  );
}

/**
 * Rejects a tag no derived type registers, unless `ignoreUnrecognizedTags`
 * lets it through. Only the tag itself is checked: an object that ends with
 * `$values` pending fails as misplaced metadata even on an abstract base
 * (measured on .NET 10), so the missing-tag rule must not run there.
 */
function throwIfUnrecognizedTag(tag: TypeDiscriminator, polymorphism: PolymorphismSpec): void {
  const isRegistered = dispatchIndexOf(polymorphism).byTag.has(tag);

  if (isRegistered || polymorphism.ignoreUnrecognizedTags) {
    return;
  }

  throw new JsonException(`Read unrecognized type discriminator id '${tag}'.`);
}

/**
 * A missing tag reads the base contract of a concrete base; an abstract or
 * interface base cannot be read.
 */
function contractWithoutTag(spec: TypeSpec, polymorphism: PolymorphismSpec): TypeSpec {
  if (polymorphism.abstract) {
    throw missingDiscriminatorException(spec);
  }

  return spec;
}

function missingDiscriminatorException(spec: TypeSpec): NotSupportedException {
  return new NotSupportedException(
    `The JSON payload for polymorphic interface or abstract type '${contractNameOf(spec)}' ` +
      "must specify a type discriminator.",
  );
}

/**
 * A tag is a JSON string or a JSON number that fits an `Int32`, which
 * System.Text.Json reads it as; any other number cannot be converted to the
 * base, and any other JSON value is not a valid metadata property.
 */
function readTag(value: unknown, spec: TypeSpec): TypeDiscriminator {
  if (typeof value === "string") {
    return value;
  }

  if (typeof value !== "number") {
    throw new JsonException(
      "The '$id', '$ref' or '$type' metadata properties must be JSON strings. " +
        `Current token type is '${tokenTypeOf(value)}'.`,
    );
  }

  if (!isInt32(value)) {
    throw cannotConvertTo(spec);
  }

  return value;
}

function isInt32(value: number): boolean {
  return Number.isInteger(value) && value >= INT32_MIN && value <= INT32_MAX;
}

/** The `JsonTokenType` name System.Text.Json reports for a non-scalar parsed value. */
function tokenTypeOf(value: unknown): string {
  if (value === null) {
    return "Null";
  }

  if (typeof value === "boolean") {
    return value ? "True" : "False";
  }

  return Array.isArray(value) ? "StartArray" : "StartObject";
}
