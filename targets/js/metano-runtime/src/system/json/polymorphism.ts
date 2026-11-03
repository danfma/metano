import type { PolymorphismSpec, TypeDiscriminator, TypeSpec } from "./types.ts";
import { contractNameOf } from "./contract-name.ts";
import { JsonException, NotSupportedException } from "./errors.ts";

/**
 * The contract a polymorphic base writes a value with, and the tag written
 * before its properties (absent: no tag).
 */
export interface SerializeTarget {
  readonly spec: TypeSpec;
  readonly tag?: TypeDiscriminator;
}

/** One registration with its contract resolved. */
interface ResolvedVariant extends SerializeTarget {
  readonly test?: (value: unknown) => boolean;
}

/** A `[PlainObject]` variant, with the guard that selects it on write. */
interface StructuralVariant extends ResolvedVariant {
  readonly test: (value: unknown) => boolean;
}

/**
 * The lookup tables of one polymorphic base, built on first use (never inside
 * a generated getter, whose specs reference each other): class variants by
 * constructor, contracts by tag (a `Map`, so `1` and `"1"` stay distinct),
 * and `[PlainObject]` variants in guard test order.
 */
interface DispatchIndex {
  readonly byConstructor: ReadonlyMap<unknown, SerializeTarget>;
  readonly byTag: ReadonlyMap<TypeDiscriminator, TypeSpec>;
  readonly structural: readonly StructuralVariant[];
}

const dispatchIndexes = new WeakMap<PolymorphismSpec, DispatchIndex>();

const INT32_MIN = -2_147_483_648;

const INT32_MAX = 2_147_483_647;

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
  }));

  const byConstructor = new Map<unknown, SerializeTarget>();
  const byTag = new Map<TypeDiscriminator, TypeSpec>();

  for (const variant of variants) {
    if (variant.spec.type) {
      byConstructor.set(variant.spec.type, { spec: variant.spec, tag: variant.tag });
    }

    if (variant.tag !== undefined) {
      byTag.set(variant.tag, variant.spec);
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

/**
 * Chooses the contract a polymorphic base reads `raw` with (FR-007): the
 * contract of a known tag, the base for a missing tag (or an ignored unknown
 * one) of a concrete base. Throws System.Text.Json's exceptions otherwise.
 */
export function resolveDeserializeTarget(
  raw: Record<string, unknown>,
  spec: TypeSpec,
  polymorphism: PolymorphismSpec,
): TypeSpec {
  if (!hasReadableTag(raw, spec, polymorphism)) {
    return contractWithoutTag(spec, polymorphism);
  }

  const tag = readTag(raw[polymorphism.discriminator], spec);
  const derived = dispatchIndexOf(polymorphism).byTag.get(tag);

  if (derived) {
    return derived;
  }

  if (polymorphism.ignoreUnrecognizedTags) {
    return contractWithoutTag(spec, polymorphism);
  }

  throw new JsonException(`Read unrecognized type discriminator id '${tag}'.`);
}

/**
 * The tag counts only under its exact (case-sensitive) name, and only as the
 * first key unless the context allows out-of-order metadata: a later tag is a
 * missing tag, as in System.Text.Json.
 */
function hasReadableTag(
  raw: Record<string, unknown>,
  spec: TypeSpec,
  polymorphism: PolymorphismSpec,
): boolean {
  const { discriminator } = polymorphism;

  if (!Object.hasOwn(raw, discriminator)) {
    return false;
  }

  return (
    spec.readOptions?.allowOutOfOrderMetadataProperties === true ||
    firstKeyOf(raw) === discriminator
  );
}

function firstKeyOf(raw: Record<string, unknown>): string | undefined {
  for (const key in raw) {
    return key;
  }

  return undefined;
}

/**
 * A missing tag reads the base contract of a concrete base; an abstract or
 * interface base cannot be read.
 */
function contractWithoutTag(spec: TypeSpec, polymorphism: PolymorphismSpec): TypeSpec {
  if (polymorphism.abstract) {
    throw new NotSupportedException(
      `The JSON payload for polymorphic interface or abstract type '${contractNameOf(spec)}' ` +
        "must specify a type discriminator.",
    );
  }

  return spec;
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
    throw new JsonException(`The JSON value could not be converted to ${contractNameOf(spec)}.`);
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
