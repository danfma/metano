import type {
  TypeDescriptor,
  TypeSpec,
  PrimitiveDescriptor,
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
  TypeDiscriminator,
} from "./types.ts";
import { HashSet } from "../collections/hash-set.ts";
import { readContainer, writeContainer } from "./container-depth.ts";
import { cannotConvertTo } from "./contract-name.ts";
import { JsonException } from "./errors.ts";
import { isDecimalText, readNumberText } from "./number-from-string.ts";
import { ordinalIgnoreCaseKeyOf } from "./ordinal-ignore-case.ts";
import {
  hasTaggedRegistration,
  resolveDeserializeTarget,
  resolveSerializeTarget,
} from "./polymorphism.ts";

// ─── Converter Resolution ────────────────────────────────────────────────────

/** The serialize/deserialize pair of one descriptor. */
type ValueConverter = {
  serialize: (value: unknown) => unknown;
  deserialize: (value: unknown) => unknown;
};

/** How a numeric type is named in a conversion error, from its CLR name. */
type ErrorTypeNaming = (clrName: string) => string;

const clrNameAsIs: ErrorTypeNaming = (clrName) => clrName;

/**
 * Resolves the serialize/deserialize pair for a given type descriptor, checking
 * custom converters first (user overrides), then falling back to built-in ones.
 * `readOptions` are the read options of the spec being read (FR-048); a `ref`
 * reads with its target spec's own options.
 */
export function resolveConverter(
  descriptor: TypeDescriptor,
  customConverters?: readonly JsonConverter[],
  readOptions?: JsonReadOptions,
  errorTypeNaming: ErrorTypeNaming = clrNameAsIs,
): ValueConverter {
  if (customConverters) {
    const custom = customConverters.find((c) => c.kind === descriptor.kind);
    if (custom) return custom;
  }
  return getBuiltinConverter(descriptor, customConverters, readOptions, errorTypeNaming);
}

// ─── Built-in Converters ─────────────────────────────────────────────────────

function getBuiltinConverter(
  descriptor: TypeDescriptor,
  customConverters: readonly JsonConverter[] | undefined,
  readOptions: JsonReadOptions | undefined,
  errorTypeNaming: ErrorTypeNaming,
): ValueConverter {
  switch (descriptor.kind) {
    case "primitive":
      return withNumberText(
        primitiveConverter,
        numberTextOf(descriptor, readOptions),
        errorTypeNaming,
      );
    case "temporal":
      return temporalConverter(descriptor);
    case "decimal":
      return withNumberText(decimalConverter, decimalTextOf(readOptions), errorTypeNaming);
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

// ─── Primitive ───────────────────────────────────────────────────────────────

const primitiveConverter = {
  serialize: (value: unknown) => value,
  deserialize: (value: unknown) => value,
};

// ─── Numbers read from strings ───────────────────────────────────────────────

/** How a numeric descriptor reads a JSON string (FR-048). */
interface NumberText {
  /** The CLR name System.Text.Json reports when the string is rejected. */
  readonly clrName: string;
  /** The value `text` spells, or `undefined` when the grammar rejects it. */
  readonly read: (text: string) => unknown;
}

function readsNumbersFromStrings(readOptions: JsonReadOptions | undefined): boolean {
  return readOptions?.numberHandling === "allowReadingFromString";
}

/** A numeric primitive reads strings when the context allows it and its `numberType` is known. */
function numberTextOf(
  descriptor: PrimitiveDescriptor,
  readOptions: JsonReadOptions | undefined,
): NumberText | undefined {
  const { numberType } = descriptor;

  if (numberType === undefined || !readsNumbersFromStrings(readOptions)) {
    return undefined;
  }

  return { clrName: numberType, read: (text) => readNumberText(text, numberType) };
}

/**
 * A decimal reads strings when the context allows it. The accepted text is
 * returned unchanged: the built-in decimal converter never builds a decimal
 * value, and the text keeps its full precision and scale (`"1.10"`).
 */
function decimalTextOf(readOptions: JsonReadOptions | undefined): NumberText | undefined {
  if (!readsNumbersFromStrings(readOptions)) {
    return undefined;
  }

  return { clrName: "System.Decimal", read: (text) => (isDecimalText(text) ? text : undefined) };
}

/**
 * `converter`, also reading JSON strings by `numberText`'s grammar; a
 * rejected string throws System.Text.Json's conversion error. Other values,
 * and writes, go to `converter` unchanged (FR-048 item 5).
 */
function withNumberText(
  converter: ValueConverter,
  numberText: NumberText | undefined,
  errorTypeNaming: ErrorTypeNaming,
): ValueConverter {
  if (numberText === undefined) {
    return converter;
  }

  return {
    serialize: converter.serialize,
    deserialize: (value: unknown) => {
      if (typeof value !== "string") {
        return converter.deserialize(value);
      }

      const number = numberText.read(value);

      if (number === undefined) {
        throw new JsonException(
          `The JSON value could not be converted to ${errorTypeNaming(numberText.clrName)}.`,
        );
      }

      return number;
    },
  };
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

// ─── Decimal ─────────────────────────────────────────────────────────────────

/**
 * Default decimal converter: serializes via toNumber() (matching C# default),
 * deserializes by constructing a new Decimal. Users who need string precision
 * can override with a custom converter.
 */
const decimalConverter = {
  serialize: (value: unknown) => {
    if (value == null) return null;
    // Decimal from decimal.js has toNumber()
    return (value as { toNumber(): number }).toNumber();
  },
  deserialize: (value: unknown) => {
    if (value == null) return null;
    // We don't import Decimal directly to avoid a hard dependency.
    // The TypeSpec's factory will handle construction. For standalone
    // deserialize, the user should provide a custom converter or the
    // generated spec handles it via the factory.
    return value;
  },
};

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

/** A rejected number string names `System.Nullable`1[<inner>]`, as System.Text.Json does. */
const nullableOf: ErrorTypeNaming = (clrName) => `System.Nullable\`1[${clrName}]`;

function nullableConverter(
  descriptor: NullableDescriptor,
  customConverters: readonly JsonConverter[] | undefined,
  readOptions: JsonReadOptions | undefined,
) {
  const innerConv = resolveConverter(descriptor.inner, customConverters, readOptions, nullableOf);

  return {
    serialize: (value: unknown) => (value == null ? null : innerConv.serialize(value)),
    deserialize: (value: unknown) => (value == null ? null : innerConv.deserialize(value)),
  };
}

// ─── Ref ─────────────────────────────────────────────────────────────────────

function refConverter(descriptor: RefDescriptor, customConverters?: readonly JsonConverter[]) {
  return {
    serialize: (value: unknown) => {
      if (value == null) return null;
      return serializeWithSpec(value, descriptor.spec(), customConverters);
    },
    deserialize: (value: unknown) => {
      if (value == null) return null;
      return deserializeWithSpec(value, descriptor.spec(), customConverters);
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
 * `ignoredProperties` (a `[JsonIgnore]` override) is left out.
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

  return wire;
}

/** The spec and its bases, most-derived first. */
function* specChainOf(spec: TypeSpec): Generator<TypeSpec> {
  yield spec;

  if (spec.base) {
    yield* specChainOf(spec.base);
  }
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
  const { polymorphism } = spec;

  if (!polymorphism) {
    return serializeContract(value, spec, customConverters);
  }

  const target = resolveSerializeTarget(value, spec, polymorphism);

  const typeMetadata =
    target.tag === undefined ? undefined : { name: polymorphism.discriminator, tag: target.tag };

  return serializeContract(value, target.spec, customConverters, typeMetadata);
}

/** The type discriminator a polymorphic write puts first. */
interface TypeMetadata {
  readonly name: string;
  readonly tag: TypeDiscriminator;
}

/** Writes `value` with exactly `spec`'s properties, after the tag when one is given. */
function serializeContract(
  value: unknown,
  spec: TypeSpec,
  customConverters?: readonly JsonConverter[],
  typeMetadata?: TypeMetadata,
): Record<string, unknown> {
  return writeContainer(() => {
    const obj = value as Record<string, unknown>;
    const result: Record<string, unknown> = {};

    if (typeMetadata) {
      result[typeMetadata.name] = typeMetadata.tag;
    }

    for (const prop of collectWireProperties(spec)) {
      const tsValue = obj[prop.ts];

      if (tsValue == null && prop.optional) {
        result[prop.json] = null;
        continue;
      }

      const conv = resolveConverter(prop.type, customConverters);
      result[prop.json] = conv.serialize(tsValue);
    }

    return result;
  });
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
  const raw = requireJsonObject(data, spec);
  const { polymorphism } = spec;

  if (!polymorphism) {
    return deserializeContract(raw, spec, customConverters);
  }

  const target = resolveDeserializeTarget(raw, spec, polymorphism);

  // A tagged hierarchy reads its discriminator as metadata, never as a property.
  const metadataKey = hasTaggedRegistration(polymorphism) ? polymorphism.discriminator : undefined;

  // The target is `spec` or one of its registered derived contracts, so it builds a `T`.
  return deserializeContract(raw, target, customConverters, metadataKey) as T;
}

/** `data` as a JSON object; any other JSON value cannot be converted to the contract. */
function requireJsonObject(data: unknown, spec: TypeSpec): Record<string, unknown> {
  if (data === null || typeof data !== "object" || Array.isArray(data)) {
    throw cannotConvertTo(spec);
  }

  return data as Record<string, unknown>;
}

/**
 * Reads `raw` with exactly `spec`'s properties and builds it with `spec`'s
 * factory, under `spec`'s read options. `metadataKey` is the discriminator a
 * polymorphic read consumed.
 */
function deserializeContract<T>(
  raw: Record<string, unknown>,
  spec: TypeSpec<T>,
  customConverters?: readonly JsonConverter[],
  metadataKey?: string,
): T {
  return readContainer("object", () => {
    const deserialized: Record<string, unknown> = {};
    const properties = collectWireProperties(spec);
    const jsonValueOf = jsonValueLookupOf(raw, properties, spec.readOptions, metadataKey);

    for (const prop of properties) {
      const jsonValue = jsonValueOf(prop);

      if (jsonValue === undefined || jsonValue === null) {
        if (prop.optional) {
          deserialized[prop.ts] = null;
          continue;
        }

        if (jsonValue === undefined) {
          throw new JsonException(`Missing required property: "${prop.json}"`);
        }
      }

      const conv = resolveConverter(prop.type, customConverters, spec.readOptions);
      deserialized[prop.ts] = conv.deserialize(jsonValue);
    }

    return spec.factory(deserialized);
  });
}

/**
 * How a property finds its JSON value: by its exact JSON name, or, under
 * `propertyNameCaseInsensitive`, by every key that matches it ignoring case
 * (`OrdinalIgnoreCase`), applied in document order so that the last match
 * wins (FR-048, measured).
 */
function jsonValueLookupOf(
  raw: Record<string, unknown>,
  properties: readonly PropertySpec[],
  readOptions: JsonReadOptions | undefined,
  metadataKey: string | undefined,
): (property: PropertySpec) => unknown {
  if (!readOptions?.propertyNameCaseInsensitive) {
    return (property) => raw[property.json];
  }

  const propertiesByKey = new Map(
    properties.map((property) => [ordinalIgnoreCaseKeyOf(property.json), property]),
  );

  const values = new Map<PropertySpec, unknown>();
  const dataEntries = Object.entries(raw).filter(([key]) => key !== metadataKey);

  for (const [key, value] of dataEntries) {
    const property = propertiesByKey.get(ordinalIgnoreCaseKeyOf(key));

    if (property) {
      values.set(property, value);
    }
  }

  return (property) => values.get(property);
}
