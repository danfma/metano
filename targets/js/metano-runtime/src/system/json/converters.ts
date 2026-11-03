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
} from "./types.ts";
import { HashSet } from "../collections/hash-set.ts";
import { readContainer, writeContainer } from "./container-depth.ts";
import { JsonException } from "./errors.ts";

// ─── Converter Resolution ────────────────────────────────────────────────────

/**
 * Resolves the serialize/deserialize pair for a given type descriptor, checking
 * custom converters first (user overrides), then falling back to built-in ones.
 */
export function resolveConverter(
  descriptor: TypeDescriptor,
  customConverters?: readonly JsonConverter[],
): { serialize: (value: unknown) => unknown; deserialize: (value: unknown) => unknown } {
  if (customConverters) {
    const custom = customConverters.find((c) => c.kind === descriptor.kind);
    if (custom) return custom;
  }
  return getBuiltinConverter(descriptor, customConverters);
}

// ─── Built-in Converters ─────────────────────────────────────────────────────

function getBuiltinConverter(
  descriptor: TypeDescriptor,
  customConverters?: readonly JsonConverter[],
): { serialize: (value: unknown) => unknown; deserialize: (value: unknown) => unknown } {
  switch (descriptor.kind) {
    case "primitive":
      return primitiveConverter;
    case "temporal":
      return temporalConverter(descriptor);
    case "decimal":
      return decimalConverter;
    case "map":
      return mapConverter(descriptor, customConverters);
    case "array":
      return arrayConverter(descriptor, customConverters);
    case "hashSet":
      return hashSetConverter(descriptor, customConverters);
    case "branded":
      return brandedConverter(descriptor);
    case "enum":
      return enumConverter(descriptor);
    case "numericEnum":
      return numericEnumConverter(descriptor);
    case "nullable":
      return nullableConverter(descriptor, customConverters);
    case "ref":
      return refConverter(descriptor, customConverters);
  }
}

// ─── Primitive ───────────────────────────────────────────────────────────────

const primitiveConverter = {
  serialize: (value: unknown) => value,
  deserialize: (value: unknown) => value,
};

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

function mapConverter(descriptor: MapDescriptor, customConverters?: readonly JsonConverter[]) {
  const keyConv = resolveConverter(descriptor.key, customConverters);
  const valueConv = resolveConverter(descriptor.value, customConverters);

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

      return readContainer("object", () => {
        const obj = value as Record<string, unknown>;
        const map = new Map<unknown, unknown>();

        for (const [k, v] of Object.entries(obj)) {
          map.set(keyConv.deserialize(k), valueConv.deserialize(v));
        }

        return map;
      });
    },
  };
}

// ─── Array ───────────────────────────────────────────────────────────────────

type ElementConverter = {
  serialize: (value: unknown) => unknown;
  deserialize: (value: unknown) => unknown;
};

/**
 * Writes any iterable (an array, a `Set`, a generator, a LINQ pipe for
 * `IEnumerable<T>`) as a JSON array, converting each element (FR-038).
 */
function serializeIterable(value: unknown, elemConv: ElementConverter): unknown[] | null {
  if (value == null) return null;

  return writeContainer(() =>
    Array.from(value as Iterable<unknown>, (item) => elemConv.serialize(item)),
  );
}

/**
 * Reads a JSON array element by element. Any other JSON value throws
 * `JsonException` (#425); `null` reads as `null`.
 */
function deserializeArray(value: unknown, elemConv: ElementConverter): unknown[] | null {
  if (value == null) return null;

  if (!Array.isArray(value)) {
    throw new JsonException(`Expected array for deserialization, got ${jsonKindOf(value)}`);
  }

  return readContainer("array", () => value.map((item) => elemConv.deserialize(item)));
}

function arrayConverter(descriptor: ArrayDescriptor, customConverters?: readonly JsonConverter[]) {
  const elemConv = resolveConverter(descriptor.element, customConverters);

  return {
    serialize: (value: unknown) => serializeIterable(value, elemConv),
    deserialize: (value: unknown) => deserializeArray(value, elemConv),
  };
}

// ─── HashSet ─────────────────────────────────────────────────────────────────

function hashSetConverter(
  descriptor: HashSetDescriptor,
  customConverters?: readonly JsonConverter[],
) {
  const elemConv = resolveConverter(descriptor.element, customConverters);

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
  customConverters?: readonly JsonConverter[],
) {
  const innerConv = resolveConverter(descriptor.inner, customConverters);

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
 * Base properties come first, own properties last.
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

/** The JSON kind of a parsed value, for error messages. */
function jsonKindOf(value: unknown): string {
  if (value === null) return "null";
  if (Array.isArray(value)) return "array";

  return typeof value;
}

/**
 * The C# type name a contract reports in errors: its constructor's name. A
 * `[PlainObject]` contract has no constructor and reports `Object`.
 */
function contractNameOf(spec: TypeSpec): string {
  return spec.type?.name ?? "Object";
}

/**
 * Serialize a TS object to a JSON-safe plain object using the given spec.
 */
export function serializeWithSpec(
  value: unknown,
  spec: TypeSpec,
  customConverters?: readonly JsonConverter[],
): Record<string, unknown> {
  return writeContainer(() => {
    const obj = value as Record<string, unknown>;
    const result: Record<string, unknown> = {};

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
 * Deserialize unknown data (from JSON.parse) into a typed instance using the spec.
 */
export function deserializeWithSpec<T>(
  data: unknown,
  spec: TypeSpec<T>,
  customConverters?: readonly JsonConverter[],
): T {
  if (data === null || typeof data !== "object" || Array.isArray(data)) {
    throw new JsonException(`The JSON value could not be converted to ${contractNameOf(spec)}.`);
  }

  return readContainer("object", () => {
    const raw = data as Record<string, unknown>;
    const deserialized: Record<string, unknown> = {};

    for (const prop of collectWireProperties(spec)) {
      const jsonValue = raw[prop.json];

      if (jsonValue === undefined || jsonValue === null) {
        if (prop.optional) {
          deserialized[prop.ts] = null;
          continue;
        }

        if (jsonValue === undefined) {
          throw new JsonException(`Missing required property: "${prop.json}"`);
        }
      }

      const conv = resolveConverter(prop.type, customConverters);
      deserialized[prop.ts] = conv.deserialize(jsonValue);
    }

    return spec.factory(deserialized);
  });
}
