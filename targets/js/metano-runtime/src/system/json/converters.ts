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
      const map = value as Map<unknown, unknown>;
      const result: Record<string, unknown> = {};
      for (const [k, v] of map) {
        const serializedKey = String(keyConv.serialize(k));
        result[serializedKey] = valueConv.serialize(v);
      }
      return result;
    },
    deserialize: (value: unknown) => {
      if (value == null) return null;
      const obj = value as Record<string, unknown>;
      const map = new Map<unknown, unknown>();
      for (const [k, v] of Object.entries(obj)) {
        map.set(keyConv.deserialize(k), valueConv.deserialize(v));
      }
      return map;
    },
  };
}

// ─── Array ───────────────────────────────────────────────────────────────────

function arrayConverter(descriptor: ArrayDescriptor, customConverters?: readonly JsonConverter[]) {
  const elemConv = resolveConverter(descriptor.element, customConverters);

  return {
    serialize: (value: unknown) => {
      if (value == null) return null;
      return (value as unknown[]).map((item) => elemConv.serialize(item));
    },
    deserialize: (value: unknown) => {
      if (value == null) return null;
      return (value as unknown[]).map((item) => elemConv.deserialize(item));
    },
  };
}

// ─── HashSet ─────────────────────────────────────────────────────────────────

function hashSetConverter(
  descriptor: HashSetDescriptor,
  customConverters?: readonly JsonConverter[],
) {
  const elemConv = resolveConverter(descriptor.element, customConverters);

  return {
    serialize: (value: unknown) => {
      if (value == null) return null;
      const set = value as HashSet<unknown>;
      const result: unknown[] = [];
      for (const item of set) {
        result.push(elemConv.serialize(item));
      }
      return result;
    },
    deserialize: (value: unknown) => {
      if (value == null) return null;
      const arr = (value as unknown[]).map((item) => elemConv.deserialize(item));
      return new HashSet(arr);
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
        throw new Error(
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
  const validValues = new Set(Object.values(descriptor.values));

  return {
    serialize: (value: unknown) => value, // already a number
    deserialize: (value: unknown) => {
      if (typeof value !== "number" || !validValues.has(value)) {
        throw new Error(
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
 * Serialize a TS object to a JSON-safe plain object using the given spec.
 */
export function serializeWithSpec(
  value: unknown,
  spec: TypeSpec,
  customConverters?: readonly JsonConverter[],
): Record<string, unknown> {
  const obj = value as Record<string, unknown>;
  const props = collectProperties(spec);
  const result: Record<string, unknown> = {};

  for (const prop of props) {
    const tsValue = obj[prop.ts];
    if (tsValue == null && prop.optional) {
      result[prop.json] = null;
      continue;
    }
    const conv = resolveConverter(prop.type, customConverters);
    result[prop.json] = conv.serialize(tsValue);
  }

  return result;
}

/**
 * Deserialize unknown data (from JSON.parse) into a typed instance using the spec.
 */
export function deserializeWithSpec<T>(
  data: unknown,
  spec: TypeSpec<T>,
  customConverters?: readonly JsonConverter[],
): T {
  if (data == null || typeof data !== "object") {
    throw new Error(
      `Expected object for deserialization, got ${data === null ? "null" : typeof data}`,
    );
  }

  const raw = data as Record<string, unknown>;
  const props = collectProperties(spec);
  const deserialized: Record<string, unknown> = {};

  for (const prop of props) {
    const jsonValue = raw[prop.json];
    if (jsonValue === undefined || jsonValue === null) {
      if (prop.optional) {
        deserialized[prop.ts] = null;
        continue;
      }
      if (jsonValue === undefined) {
        throw new Error(`Missing required property: "${prop.json}"`);
      }
    }
    const conv = resolveConverter(prop.type, customConverters);
    deserialized[prop.ts] = conv.deserialize(jsonValue);
  }

  return spec.factory(deserialized);
}
