/**
 * The spec plans (#456, split out by #462): each contract's wire properties
 * and converters compiled once per set of custom converters and kind of tree
 * numbers, and the `serializeWithSpec` / `deserializeWithSpec` /
 * `*WithValueSpec` entry points that run them. Converters receive the plans
 * as their context (`converters.ts` imports only their type), so the two
 * modules import each other in one direction at run time.
 */
import type {
  TypeSpec,
  PropertySpec,
  JsonConverter,
  PolymorphismSpec,
  TypeDiscriminator,
  ValueSpec,
} from "./types.ts";
import type { ValueConverter } from "./value-converter.ts";
// Only the contract walks (the hot path: one container per object) open and
// close containers with enter/exit; the collection converters keep the
// closure-based `readContainer` / `writeContainer` (#456).
import { enterReadContainer, enterWriteContainer, exitContainer } from "./container-depth.ts";
import { cannotConvertTo, contractNameOf } from "./contract-name.ts";
import { valueTypeNameOf } from "./null-reads.ts";
import { type ConverterSetCache, createConverterSetCache } from "./converter-set-cache.ts";
import { JsonException } from "./errors.ts";
import { numberOfTreeValue } from "./exact-json-number.ts";
import type { ErrorTypeName } from "./numeric-converters.ts";
import {
  hasTaggedRegistration,
  resolveDeserializeTarget,
  resolveSerializeTarget,
} from "./polymorphism.ts";
import { type PropertyLookup, propertyLookupPlanOf } from "./property-lookup.ts";
import { resolveConverter } from "./converters.ts";

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
export interface SpecPlan {
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

/**
 * How the tree being read holds its numbers: `parsed`, each a `number`, as
 * `JSON.parse` builds it; `exact`, some as leaves, as `readJsonText` builds
 * it (#457): a `JsonNumberText` for those with more significant digits than
 * a double holds and, for a spec that reads a 64-bit integer, a
 * `JsonNonIntegerNumber` for the other ones with a fraction or an exponent
 * (`NumberTextLeaves`).
 */
export type TreeNumbers = "parsed" | "exact";

/**
 * The plans compiled under one set of custom converters for one kind of
 * tree numbers, each built once.
 */
export interface SpecPlans {
  readonly customConverters: readonly JsonConverter[];
  readonly treeNumbers: TreeNumbers;
  planOf(spec: TypeSpec): SpecPlan;
  valueConverterOf(spec: ValueSpec): ValueConverter;
}

/**
 * The plans of every custom-converter set, per kind of tree numbers. A plan
 * holds converters resolved against its set, so the set is part of the key:
 * plans are cached per spec inside each set's entry
 * (`converter-set-cache.ts`). Only `deserializeText` reads exact trees, so
 * the `exact` plans exist only for the specs it reads that way.
 */
const specPlansByTreeNumbers: Readonly<Record<TreeNumbers, ConverterSetCache<SpecPlans>>> = {
  parsed: /* @__PURE__ */ createConverterSetCache((converters) =>
    createSpecPlans(converters, "parsed"),
  ),
  exact: /* @__PURE__ */ createConverterSetCache((converters) =>
    createSpecPlans(converters, "exact"),
  ),
};

function specPlansFor(
  customConverters: readonly JsonConverter[] | undefined,
  treeNumbers: TreeNumbers = "parsed",
): SpecPlans {
  return specPlansByTreeNumbers[treeNumbers].entryFor(customConverters);
}

function createSpecPlans(
  customConverters: readonly JsonConverter[],
  treeNumbers: TreeNumbers,
): SpecPlans {
  const specPlans = new WeakMap<TypeSpec, SpecPlan>();
  const valueConverters = new WeakMap<ValueSpec, ValueConverter>();

  const plans: SpecPlans = {
    customConverters,
    treeNumbers,
    planOf: (spec) => cachedIn(specPlans, spec, () => compileSpecPlan(spec, plans)),
    valueConverterOf: (spec) =>
      cachedIn(valueConverters, spec, () =>
        resolveConverter(spec.descriptor, plans, spec.readOptions),
      ),
  };

  return plans;
}

/**
 * How a property's conversion error names the declaring contract, as
 * System.Text.Json does (#485): `{"At":"03:04:60"}` on a `HolderTimeOnly`
 * names `HolderTimeOnly`, while a value root or an element names the
 * temporal type itself.
 */
function contractErrorNameOf(spec: TypeSpec): ErrorTypeName {
  return () => contractNameOf(spec);
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
function compileSpecPlan(spec: TypeSpec, plans: SpecPlans): SpecPlan {
  const properties = collectWireProperties(spec);

  const assignedIfPresent = new Set(spec.assignIfPresent);

  const propertyPlans = properties.map<PropertyPlan>((property) => ({
    property,
    converter: resolveConverter(property.type, plans, spec.readOptions, contractErrorNameOf(spec)),
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

      if (tsValue == null && property.omitWhenNull) {
        continue;
      }

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
 * (FR-007). `treeNumbers` says how `data` holds its numbers; a root number
 * leaf reads as the number it is, which no contract accepts.
 */
export function deserializeWithSpec<T>(
  data: unknown,
  spec: TypeSpec<T>,
  customConverters?: readonly JsonConverter[],
  treeNumbers: TreeNumbers = "parsed",
): T {
  const plan = specPlansFor(customConverters, treeNumbers).planOf(spec);
  const root = treeNumbers === "exact" ? numberOfTreeValue(data) : data;

  // The plan reads `spec` or one of its registered derived contracts, so it builds a `T`.
  return plan.deserialize(root) as T;
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
  const readProperties = propertyReaderOf(spec, propertyPlans);
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
 * list is left out; a `null` on a non-nullable value-type property throws
 * naming the declaring contract, as System.Text.Json does (#471).
 */
function propertyReaderOf(
  spec: TypeSpec,
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

      if (
        jsonValue === null &&
        !property.optional &&
        valueTypeNameOf(property.type) !== undefined
      ) {
        throw cannotConvertTo(spec);
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

/**
 * Deserializes a non-object root through its descriptor, under the spec's
 * read options. `treeNumbers` says how `data` holds its numbers.
 */
export function deserializeWithValueSpec(
  data: unknown,
  spec: ValueSpec,
  customConverters?: readonly JsonConverter[],
  treeNumbers: TreeNumbers = "parsed",
): unknown {
  return specPlansFor(customConverters, treeNumbers).valueConverterOf(spec).deserialize(data);
}

// ─── Memoization ─────────────────────────────────────────────────────────────

function cachedIn<K extends object, V>(cache: WeakMap<K, V>, key: K, build: () => V): V {
  const cached = cache.get(key);

  if (cached !== undefined) {
    return cached;
  }

  const built = build();
  cache.set(key, built);

  return built;
}
