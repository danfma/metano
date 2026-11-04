import type {
  TypeDescriptor,
  TypeSpec,
  PropertySpec,
  ArrayDescriptor,
  BrandedDescriptor,
  EnumDescriptor,
  HashSetDescriptor,
  MapDescriptor,
  NullableDescriptor,
  NumericEnumDescriptor,
  RefDescriptor,
  TemporalDescriptor,
  JsonConverter,
  JsonReadOptions,
  PolymorphismSpec,
  TypeDiscriminator,
  ValueSpec,
} from "./types.ts";
import type { ValueConverter } from "./value-converter.ts";
import { byteArrayConverter } from "./byte-array-converter.ts";
import { HashSet } from "../collections/hash-set.ts";
// Only the contract walks (the hot path: one container per object) open and
// close containers with enter/exit; the collection converters keep the
// closure-based `readContainer` / `writeContainer` (#456).
import {
  enterReadContainer,
  enterWriteContainer,
  exitContainer,
  readContainer,
  writeContainer,
} from "./container-depth.ts";
import { cannotConvertTo } from "./contract-name.ts";
import { createConverterSetCache } from "./converter-set-cache.ts";
import { JsonException } from "./errors.ts";
import {
  decimalConverterOf,
  nullableTypeName,
  primitiveConverterOf,
} from "./numeric-converters.ts";
import {
  hasTaggedRegistration,
  resolveDeserializeTarget,
  resolveSerializeTarget,
} from "./polymorphism.ts";
import { type PropertyLookup, propertyLookupPlanOf } from "./property-lookup.ts";

// ─── Converter Resolution ────────────────────────────────────────────────────

/**
 * Resolves the serialize/deserialize pair for a given type descriptor, checking
 * custom converters first (user overrides), then falling back to built-in ones.
 * `readOptions` are the read options of the spec being read (FR-048); a `ref`
 * reads with its target spec's own options.
 */
function resolveConverter(
  descriptor: TypeDescriptor,
  customConverters?: readonly JsonConverter[],
  readOptions?: JsonReadOptions,
): ValueConverter {
  return (
    customConverterFor(descriptor, customConverters) ??
    getBuiltinConverter(descriptor, customConverters, readOptions)
  );
}

function customConverterFor(
  descriptor: TypeDescriptor,
  customConverters: readonly JsonConverter[] | undefined,
): JsonConverter | undefined {
  return customConverters?.find((converter) => converter.kind === descriptor.kind);
}

// ─── Built-in Converters ─────────────────────────────────────────────────────

function getBuiltinConverter(
  descriptor: TypeDescriptor,
  customConverters: readonly JsonConverter[] | undefined,
  readOptions: JsonReadOptions | undefined,
): ValueConverter {
  switch (descriptor.kind) {
    case "primitive":
      return primitiveConverterOf(descriptor, readOptions);
    case "temporal":
      return temporalConverter(descriptor);
    case "decimal":
      return decimalConverterOf(descriptor, readOptions);
    case "bytes":
      return byteArrayConverter;
    case "map":
      return mapConverter(descriptor, customConverters, readOptions);
    case "array":
      return arrayConverter(descriptor, customConverters, readOptions);
    case "hashSet":
      return hashSetConverter(descriptor, customConverters, readOptions);
    case "branded":
      return brandedConverter(descriptor);
    case "enum":
      return enumConverter(descriptor);
    case "numericEnum":
      return numericEnumConverter(descriptor);
    case "nullable":
      return nullableConverter(descriptor, customConverters, readOptions);
    case "ref":
      return refConverter(descriptor, customConverters);
  }
}

// ─── Temporal ────────────────────────────────────────────────────────────────

function temporalConverter(descriptor: TemporalDescriptor) {
  return {
    serialize: (value: unknown) => {
      if (value == null) return null;
      return String(value);
    },
    deserialize: (value: unknown) => {
      if (value == null) return null;
      return descriptor.parse(value as string);
    },
  };
}

// ─── Map ─────────────────────────────────────────────────────────────────────

/**
 * Dictionary keys are always JSON strings, so the read options apply to the
 * values only.
 */
function mapConverter(
  descriptor: MapDescriptor,
  customConverters: readonly JsonConverter[] | undefined,
  readOptions: JsonReadOptions | undefined,
) {
  const keyConv = resolveConverter(descriptor.key, customConverters);
  const valueConv = resolveConverter(descriptor.value, customConverters, readOptions);

  return {
    serialize: (value: unknown) => {
      if (value == null) return null;

      return writeContainer(() => {
        const map = value as Map<unknown, unknown>;
        const result: Record<string, unknown> = {};

        for (const [k, v] of map) {
          const serializedKey = String(keyConv.serialize(k));
          result[serializedKey] = valueConv.serialize(v);
        }

        return result;
      });
    },
    deserialize: (value: unknown) => {
      if (value == null) return null;

      if (typeof value !== "object" || Array.isArray(value)) {
        throw unexpectedJsonKind("object", value);
      }

      return readContainer("object", () => {
        const map = new Map<unknown, unknown>();

        for (const [k, v] of Object.entries(value)) {
          map.set(keyConv.deserialize(k), valueConv.deserialize(v));
        }

        return map;
      });
    },
  };
}

// ─── Collection input ────────────────────────────────────────────────────────

/**
 * The error for a non-null JSON value of the wrong kind for a collection or
 * dictionary (#425). STJ names the CLR collection type, which a descriptor
 * does not carry.
 */
function unexpectedJsonKind(expected: "array" | "object", value: unknown): JsonException {
  const actual = Array.isArray(value) ? "array" : typeof value;

  return new JsonException(`Expected ${expected} for deserialization, got ${actual}`);
}

// ─── Array ───────────────────────────────────────────────────────────────────

/**
 * Writes any iterable (an array, a `Set`, a generator, a LINQ pipe for
 * `IEnumerable<T>`) as a JSON array, converting each element (FR-038).
 */
function serializeIterable(value: unknown, elemConv: ValueConverter): unknown[] | null {
  if (value == null) return null;

  return writeContainer(() =>
    Array.from(value as Iterable<unknown>, (item) => elemConv.serialize(item)),
  );
}

/**
 * Reads a JSON array element by element. Any other JSON value throws
 * `JsonException` (#425); `null` reads as `null`.
 */
function deserializeArray(value: unknown, elemConv: ValueConverter): unknown[] | null {
  if (value == null) return null;

  if (!Array.isArray(value)) {
    throw unexpectedJsonKind("array", value);
  }

  return readContainer("array", () => value.map((item) => elemConv.deserialize(item)));
}

function arrayConverter(
  descriptor: ArrayDescriptor,
  customConverters: readonly JsonConverter[] | undefined,
  readOptions: JsonReadOptions | undefined,
) {
  const elemConv = resolveConverter(descriptor.element, customConverters, readOptions);

  return {
    serialize: (value: unknown) => serializeIterable(value, elemConv),
    deserialize: (value: unknown) => deserializeArray(value, elemConv),
  };
}

// ─── HashSet ─────────────────────────────────────────────────────────────────

function hashSetConverter(
  descriptor: HashSetDescriptor,
  customConverters: readonly JsonConverter[] | undefined,
  readOptions: JsonReadOptions | undefined,
) {
  const elemConv = resolveConverter(descriptor.element, customConverters, readOptions);

  return {
    serialize: (value: unknown) => serializeIterable(value, elemConv),
    deserialize: (value: unknown) => {
      const items = deserializeArray(value, elemConv);

      return items === null ? null : new HashSet(items);
    },
  };
}

// ─── Branded ─────────────────────────────────────────────────────────────────

function brandedConverter(descriptor: BrandedDescriptor) {
  return {
    serialize: (value: unknown) => value, // brand erases at runtime
    deserialize: (value: unknown) => descriptor.create(value),
  };
}

// ─── Enum (String) ───────────────────────────────────────────────────────────

function enumConverter(descriptor: EnumDescriptor) {
  const validValues = new Set(Object.values(descriptor.values));

  return {
    serialize: (value: unknown) => value, // already a string
    deserialize: (value: unknown) => {
      if (typeof value !== "string" || !validValues.has(value)) {
        throw new JsonException(
          `Invalid enum value: ${JSON.stringify(value)}. ` +
            `Expected one of: ${[...validValues].join(", ")}`,
        );
      }
      return value;
    },
  };
}

// ─── Enum (Numeric) ──────────────────────────────────────────────────────────

function numericEnumConverter(descriptor: NumericEnumDescriptor) {
  const validValues = new Set(
    Object.values(descriptor.values).filter((value) => typeof value === "number"),
  );

  return {
    serialize: (value: unknown) => value, // already a number
    deserialize: (value: unknown) => {
      if (typeof value !== "number" || !validValues.has(value)) {
        throw new JsonException(
          `Invalid numeric enum value: ${JSON.stringify(value)}. ` +
            `Expected one of: ${[...validValues].join(", ")}`,
        );
      }
      return value;
    },
  };
}

// ─── Nullable ────────────────────────────────────────────────────────────────

function nullableConverter(
  descriptor: NullableDescriptor,
  customConverters: readonly JsonConverter[] | undefined,
  readOptions: JsonReadOptions | undefined,
) {
  const innerConv =
    customConverterFor(descriptor.inner, customConverters) ??
    builtinNullableInnerConverter(descriptor.inner, customConverters, readOptions);

  return {
    serialize: (value: unknown) => (value == null ? null : innerConv.serialize(value)),
    deserialize: (value: unknown) => (value == null ? null : innerConv.deserialize(value)),
  };
}

/**
 * The built-in converter of a nullable's inner type: a numeric one names
 * ``System.Nullable`1[<CLR name>]`` in its errors, as System.Text.Json does.
 */
function builtinNullableInnerConverter(
  inner: TypeDescriptor,
  customConverters: readonly JsonConverter[] | undefined,
  readOptions: JsonReadOptions | undefined,
): ValueConverter {
  switch (inner.kind) {
    case "primitive":
      return primitiveConverterOf(inner, readOptions, nullableTypeName);
    case "decimal":
      return decimalConverterOf(inner, readOptions, nullableTypeName);
    default:
      return getBuiltinConverter(inner, customConverters, readOptions);
  }
}

// ─── Ref ─────────────────────────────────────────────────────────────────────

/**
 * A nested contract. Its spec is resolved on the first value converted, not
 * when the converter is built, so specs that reference each other (a lazy
 * getter, a cycle) compile without recursion; its plan is then reused.
 *
 * The `spec()` thunk is called once and its result kept, so it must return
 * the same spec every time, as the generated `??=` context getters do.
 */
function refConverter(descriptor: RefDescriptor, customConverters?: readonly JsonConverter[]) {
  const plans = specPlansFor(customConverters);
  const targetPlan = lazy(() => plans.planOf(descriptor.spec()));

  return {
    serialize: (value: unknown) => {
      if (value == null) return null;
      return targetPlan().serialize(value);
    },
    deserialize: (value: unknown) => {
      if (value == null) return null;
      return targetPlan().deserialize(value);
    },
  };
}

// ─── Core serialize/deserialize by spec ──────────────────────────────────────

/**
 * Collects all properties from a spec chain, walking the base hierarchy.
 * Base properties come first, own properties last: the declaration order a
 * factory reads, **not** the wire order (see `collectWireProperties`). An
 * override appears once per spec that declares it.
 */
export function collectProperties(spec: TypeSpec): readonly PropertySpec[] {
  const base = spec.base ? collectProperties(spec.base) : [];
  return [...base, ...spec.properties];
}

/**
 * The properties a spec writes and reads, in System.Text.Json's wire order
 * (FR-009): the spec's own properties, then its base's, recursively. A JSON
 * name appears once, from its most-derived declaration, and a base
 * declaration whose `ts` name a more-derived spec lists in
 * `ignoredProperties` (a `[JsonIgnore]` override) is left out. When any
 * property has an `order` (`[JsonPropertyOrder]`), the list is then sorted by
 * it, stably, across the whole chain (measured: `{"Der2":20,"Base3":3,"Der1":10,
 * "Base1":1,"Der3":30,"Base2":2}` for orders -5, -5, 0, 0, 1, 5).
 */
export function collectWireProperties(spec: TypeSpec): readonly PropertySpec[] {
  const wire: PropertySpec[] = [];
  const writtenNames = new Set<string>();
  const ignoredNames = new Set<string>();

  for (const contract of specChainOf(spec)) {
    const visible = contract.properties.filter(
      (property) => !ignoredNames.has(property.ts) && !writtenNames.has(property.json),
    );

    for (const property of visible) {
      writtenNames.add(property.json);
      wire.push(property);
    }

    for (const name of contract.ignoredProperties ?? []) {
      ignoredNames.add(name);
    }
  }

  return wire.some(hasExplicitOrder) ? wire.sort(byPropertyOrder) : wire;
}

function hasExplicitOrder(property: PropertySpec): boolean {
  return property.order !== undefined;
}

function byPropertyOrder(left: PropertySpec, right: PropertySpec): number {
  return (left.order ?? 0) - (right.order ?? 0);
}

/** The spec and its bases, most-derived first. */
function* specChainOf(spec: TypeSpec): Generator<TypeSpec> {
  yield spec;

  if (spec.base) {
    yield* specChainOf(spec.base);
  }
}

// ─── Spec plans ──────────────────────────────────────────────────────────────

/**
 * Everything the reads and writes of one spec share, compiled on its first
 * use (#456): the wire properties (`collectWireProperties`, with ignored
 * overrides), their converters resolved under the spec's read options
 * (numbers from strings included), and the case-insensitive key index. A
 * polymorphic base dispatches per value through its own cached dispatch
 * index (`polymorphism.ts`), then runs the chosen contract's plan.
 *
 * Writes use the converters resolved for reads: read options never change
 * how a value is written (FR-048 item 5).
 */
interface SpecPlan {
  /** Writes through the spec, dispatching a polymorphic base (FR-005, FR-006). */
  readonly serialize: (value: unknown) => Record<string, unknown>;
  /** Reads through the spec, dispatching a polymorphic base (FR-007). */
  readonly deserialize: (data: unknown) => unknown;
  /** Writes `value` with exactly the spec's properties, after the tag when one is given. */
  readonly writeContract: (value: unknown, typeMetadata?: TypeMetadata) => Record<string, unknown>;
  /**
   * Reads `raw` with exactly the spec's properties and builds it with the
   * spec's factory. `metadataKey` is the discriminator a polymorphic read
   * consumed.
   */
  readonly readContract: (raw: Record<string, unknown>, metadataKey?: string) => unknown;
}

/**
 * One property of a plan, with its resolved converter, and whether the
 * contract sets it after construction only when the payload carries it
 * (`TypeSpec.assignIfPresent`).
 */
interface PropertyPlan {
  readonly property: PropertySpec;
  readonly converter: ValueConverter;
  readonly assignIfPresent: boolean;
}

/** The plans compiled under one set of custom converters, each built once. */
interface SpecPlans {
  planOf(spec: TypeSpec): SpecPlan;
  valueConverterOf(spec: ValueSpec): ValueConverter;
}

/**
 * The plans of every custom-converter set. A plan holds converters resolved
 * against its set, so the set is part of the key: plans are cached per spec
 * inside each set's entry (`converter-set-cache.ts`).
 */
const specPlansByConverterSet = createConverterSetCache(createSpecPlans);

function specPlansFor(customConverters: readonly JsonConverter[] | undefined): SpecPlans {
  return specPlansByConverterSet.entryFor(customConverters);
}

function createSpecPlans(customConverters: readonly JsonConverter[]): SpecPlans {
  const specPlans = new WeakMap<TypeSpec, SpecPlan>();
  const valueConverters = new WeakMap<ValueSpec, ValueConverter>();

  const plans: SpecPlans = {
    planOf: (spec) =>
      cachedIn(specPlans, spec, () => compileSpecPlan(spec, customConverters, plans)),
    valueConverterOf: (spec) =>
      cachedIn(valueConverters, spec, () =>
        resolveConverter(spec.descriptor, customConverters, spec.readOptions),
      ),
  };

  return plans;
}

/**
 * Compiles `spec` without calling into it: nested specs and derived
 * contracts are resolved when a value first reaches them, so the plan of a
 * recursive spec never compiles itself.
 *
 * Every property's converter is resolved here, on the spec's first use, so a
 * malformed descriptor (a hand-written spec error) fails then, even when the
 * value lacks that property.
 */
function compileSpecPlan(
  spec: TypeSpec,
  customConverters: readonly JsonConverter[],
  plans: SpecPlans,
): SpecPlan {
  const properties = collectWireProperties(spec);

  const assignedIfPresent = new Set(spec.assignIfPresent);

  const propertyPlans = properties.map<PropertyPlan>((property) => ({
    property,
    converter: resolveConverter(property.type, customConverters, spec.readOptions),
    assignIfPresent: assignedIfPresent.has(property.ts),
  }));

  const writeContract = contractWriterOf(propertyPlans);
  const readContract = contractReaderOf(spec, propertyPlans);
  const { polymorphism } = spec;

  if (!polymorphism) {
    return {
      serialize: (value) => writeContract(value),
      deserialize: (data) => readContract(requireJsonObject(data, spec)),
      writeContract,
      readContract,
    };
  }

  return {
    serialize: (value) => serializePolymorphic(value, spec, polymorphism, plans),
    deserialize: (data) => deserializePolymorphic(data, spec, polymorphism, plans),
    writeContract,
    readContract,
  };
}

/**
 * Serialize a TS object to a JSON-safe plain object using the given spec. A
 * polymorphic spec first chooses the derived contract and writes its tag
 * before every property (FR-005); the chosen contract is never dispatched
 * again (FR-006).
 */
export function serializeWithSpec(
  value: unknown,
  spec: TypeSpec,
  customConverters?: readonly JsonConverter[],
): Record<string, unknown> {
  return specPlansFor(customConverters).planOf(spec).serialize(value);
}

function serializePolymorphic(
  value: unknown,
  spec: TypeSpec,
  polymorphism: PolymorphismSpec,
  plans: SpecPlans,
): Record<string, unknown> {
  const target = resolveSerializeTarget(value, spec, polymorphism);

  const typeMetadata =
    target.tag === undefined ? undefined : { name: polymorphism.discriminator, tag: target.tag };

  return plans.planOf(target.spec).writeContract(value, typeMetadata);
}

/** The type discriminator a polymorphic write puts first. */
interface TypeMetadata {
  readonly name: string;
  readonly tag: TypeDiscriminator;
}

/** Writes a value with exactly the given properties, after the tag when one is given. */
function contractWriterOf(propertyPlans: readonly PropertyPlan[]): SpecPlan["writeContract"] {
  const writeProperties = propertyWriterOf(propertyPlans);

  return (value, typeMetadata) => {
    enterWriteContainer();

    try {
      const result: Record<string, unknown> = {};

      if (typeMetadata) {
        result[typeMetadata.name] = typeMetadata.tag;
      }

      writeProperties(value as Record<string, unknown>, result);

      return result;
    } finally {
      exitContainer();
    }
  };
}

/** Writes every property of `source` into `result`, in wire order. */
function propertyWriterOf(
  propertyPlans: readonly PropertyPlan[],
): (source: Record<string, unknown>, result: Record<string, unknown>) => void {
  return (source, result) => {
    for (const { property, converter } of propertyPlans) {
      const tsValue = source[property.ts];

      if (tsValue == null && property.optional) {
        result[property.json] = null;
        continue;
      }

      result[property.json] = converter.serialize(tsValue);
    }
  };
}

/**
 * Deserialize unknown data (from JSON.parse) into a typed instance using the
 * spec. A polymorphic spec reads the derived contract its tag selects
 * (FR-007).
 */
export function deserializeWithSpec<T>(
  data: unknown,
  spec: TypeSpec<T>,
  customConverters?: readonly JsonConverter[],
): T {
  // The plan reads `spec` or one of its registered derived contracts, so it builds a `T`.
  return specPlansFor(customConverters).planOf(spec).deserialize(data) as T;
}

function deserializePolymorphic(
  data: unknown,
  spec: TypeSpec,
  polymorphism: PolymorphismSpec,
  plans: SpecPlans,
): unknown {
  const raw = requireJsonObject(data, spec);
  const target = resolveDeserializeTarget(raw, spec, polymorphism);
  const metadataKey = consumedMetadataKeyOf(polymorphism);

  return plans.planOf(target).readContract(raw, metadataKey);
}

/**
 * The discriminator a polymorphic read consumes as metadata, never as a
 * property; none for a hierarchy without tags, which reads like a
 * non-polymorphic contract.
 */
function consumedMetadataKeyOf(polymorphism: PolymorphismSpec): string | undefined {
  return hasTaggedRegistration(polymorphism) ? polymorphism.discriminator : undefined;
}

/** `data` as a JSON object; any other JSON value cannot be converted to the contract. */
function requireJsonObject(data: unknown, spec: TypeSpec): Record<string, unknown> {
  if (data === null || typeof data !== "object" || Array.isArray(data)) {
    throw cannotConvertTo(spec);
  }

  return data as Record<string, unknown>;
}

/**
 * Reads a JSON object with exactly the given properties of `spec` and builds
 * it with `spec`'s factory, under `spec`'s read options.
 */
function contractReaderOf(
  spec: TypeSpec,
  propertyPlans: readonly PropertyPlan[],
): SpecPlan["readContract"] {
  const properties = propertyPlans.map(({ property }) => property);
  const lookupPlan = propertyLookupPlanOf(properties, spec.readOptions);
  const readProperties = propertyReaderOf(propertyPlans);
  const assignPresentMembers = presentMemberAssignerOf(propertyPlans);

  return (raw, metadataKey) => {
    enterReadContainer("object");

    try {
      const deserialized = readProperties(lookupPlan(raw, metadataKey));

      return assignPresentMembers(spec.factory(deserialized), deserialized);
    } finally {
      exitContainer();
    }
  };
}

/**
 * Sets every member of the contract's `assignIfPresent` list the payload
 * carried on the constructed value, in wire order (System.Text.Json follows
 * the payload's order; the order only shows through setter side effects).
 */
function presentMemberAssignerOf(
  propertyPlans: readonly PropertyPlan[],
): (value: unknown, deserialized: Record<string, unknown>) => unknown {
  const names = propertyPlans
    .filter(({ assignIfPresent }) => assignIfPresent)
    .map(({ property }) => property.ts);

  if (names.length === 0) {
    return (value) => value;
  }

  return (value, deserialized) => {
    const target = value as Record<string, unknown>;

    for (const name of names) {
      if (name in deserialized) {
        target[name] = deserialized[name];
      }
    }

    return value;
  };
}

/**
 * Reads every property through `jsonValueOf` into the factory's input. An
 * absent (missing or `null`) optional property reads as `null`; a missing
 * required one throws; a missing one of the contract's `assignIfPresent`
 * list is left out.
 */
function propertyReaderOf(
  propertyPlans: readonly PropertyPlan[],
): (jsonValueOf: PropertyLookup) => Record<string, unknown> {
  return (jsonValueOf) => {
    const deserialized: Record<string, unknown> = {};

    for (const { property, converter, assignIfPresent } of propertyPlans) {
      const jsonValue = jsonValueOf(property);

      if (jsonValue === undefined && assignIfPresent) {
        continue;
      }

      const isAbsent = jsonValue == null;

      if (isAbsent && property.optional) {
        deserialized[property.ts] = null;
        continue;
      }

      if (jsonValue === undefined) {
        throw new JsonException(`Missing required property: "${property.json}"`);
      }

      deserialized[property.ts] = converter.deserialize(jsonValue);
    }

    return deserialized;
  };
}

// ─── Value specs ─────────────────────────────────────────────────────────────

/**
 * Serializes a non-object root through its descriptor, exactly as a property
 * of that type would be (custom converters still match by kind).
 */
export function serializeWithValueSpec(
  value: unknown,
  spec: ValueSpec,
  customConverters?: readonly JsonConverter[],
): unknown {
  return specPlansFor(customConverters).valueConverterOf(spec).serialize(value);
}

/** Deserializes a non-object root through its descriptor, under the spec's read options. */
export function deserializeWithValueSpec(
  data: unknown,
  spec: ValueSpec,
  customConverters?: readonly JsonConverter[],
): unknown {
  return specPlansFor(customConverters).valueConverterOf(spec).deserialize(data);
}

// ─── Memoization ─────────────────────────────────────────────────────────────

/** `create()` on first call, its result afterwards (a failed call is retried). */
function lazy<T>(create: () => T): () => T {
  const memo: { value?: T } = {};

  return () => {
    if (!("value" in memo)) {
      memo.value = create();
    }

    return memo.value as T;
  };
}

function cachedIn<K extends object, V>(cache: WeakMap<K, V>, key: K, build: () => V): V {
  const cached = cache.get(key);

  if (cached !== undefined) {
    return cached;
  }

  const built = build();
  cache.set(key, built);

  return built;
}
