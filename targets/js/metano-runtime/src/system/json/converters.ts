import type {
  TypeDescriptor,
  ArrayDescriptor,
  IntegerTypeName,
  BrandedDescriptor,
  EnumDescriptor,
  KeyedCollectionDescriptor,
  NullableDescriptor,
  NumericEnumDescriptor,
  RefDescriptor,
  JsonConverter,
  JsonReadOptions,
} from "./types.ts";
import type { ValueConverter } from "./value-converter.ts";
import { byteArrayConverter } from "./byte-array-converter.ts";
// The collection converters keep the closure-based `readContainer` /
// `writeContainer`; only the contract walks of `spec-plans.ts` (the hot path:
// one container per object) open and close containers with enter/exit (#456).
import { readContainer, writeContainer } from "./container-depth.ts";
import { cannotConvertToTypeName } from "./contract-name.ts";
import { dictionaryKeyConverterOf } from "./dictionary-keys.ts";
import { readNull, valueTypeNameOf } from "./null-reads.ts";
import { JsonException } from "./errors.ts";
import { exactBigIntLeafOf, numberOfTreeValue, treeWithDoubles } from "./exact-json-number.ts";
import { forwardsNumberTexts } from "./exact-number-reads.ts";
import {
  DEFAULT_ENUM_INTEGER_TYPE,
  holdsBigInts,
  type Int64EnumDescriptor,
} from "./numeric-enum-members.ts";
import { isWithinIntegerRange } from "./number-from-string.ts";
import {
  decimalConverterOf,
  type ErrorTypeName,
  int64ConverterOf,
  nullableTypeName,
  declaredTypeName,
  primitiveConverterOf,
} from "./numeric-converters.ts";
import { temporalConverterOf } from "./temporal-reads.ts";
import type { SpecPlans } from "./spec-plans.ts";

// ─── Converter Resolution ────────────────────────────────────────────────────

/**
 * Resolves the serialize/deserialize pair for a given type descriptor, checking
 * the custom converters of `plans` first (user overrides), then falling back to
 * built-in ones. `readOptions` are the read options of the spec being read
 * (FR-048); a `ref` reads with its target spec's own options. `errorTypeName`
 * names the type a built-in conversion error reports: a property names the
 * declaring contract, every other position names the value's own CLR type
 * (#485). Under exact tree numbers, only the built-in converters that read a
 * number's exact text see a number leaf (`withNumberTextsAsDoubles`).
 */
export function resolveConverter(
  descriptor: TypeDescriptor,
  plans: SpecPlans,
  readOptions?: JsonReadOptions,
  errorTypeName: ErrorTypeName = declaredTypeName,
): ValueConverter {
  return customOrBuiltinConverter(descriptor, plans, () =>
    getBuiltinConverter(descriptor, plans, readOptions, errorTypeName),
  );
}

/** The custom converter of the descriptor's kind, else `builtinOf()`, under the plans' tree numbers. */
function customOrBuiltinConverter(
  descriptor: TypeDescriptor,
  plans: SpecPlans,
  builtinOf: () => ValueConverter,
): ValueConverter {
  const custom = customConverterFor(descriptor, plans.customConverters);

  if (custom) {
    return withNumberTextsAsDoubles(custom, plans, treeWithDoubles);
  }

  const builtin = builtinOf();

  if (forwardsNumberTexts(descriptor)) {
    return builtin;
  }

  const asDoubles = handsChildrenToConverters(descriptor) ? numberOfTreeValue : treeWithDoubles;

  return withNumberTextsAsDoubles(builtin, plans, asDoubles);
}

/**
 * Whether the built-in converter of `descriptor` reads each child of its
 * value through a child converter, which reads that child's leaves in turn.
 * Every other built-in converter gets a scalar, or the whole subtree of an
 * invalid or passthrough value (an `object` slot, FR-056, a `[Branded]`
 * factory's input, an enum's error), which it must see as `JSON.parse`
 * builds it.
 */
function handsChildrenToConverters(descriptor: TypeDescriptor): boolean {
  switch (descriptor.kind) {
    case "array":
    case "hashSet":
    case "list":
    case "queue":
    case "stack":
    case "immutableList":
    case "immutableArray":
    case "readOnlyList":
    case "immutableHashSet":
    case "sortedSet":
    case "dictionary":
    case "immutableDictionary":
    case "concurrentDictionary":
    case "sortedDictionary":
    case "sortedList":
    case "ref":
      return true;
    default:
      return false;
  }
}

/**
 * `converter`, reading the number leaves of an exact tree as the `number`
 * `JSON.parse` gives (#457), through `asDoubles`: a container or `ref`
 * converter needs only its own value converted, since it hands each child
 * to a child converter that converts it in turn; a custom one and every
 * other built-in one get the whole subtree converted (`treeWithDoubles`).
 * A parsed tree holds no leaf, so its converters stay as they are.
 */
function withNumberTextsAsDoubles(
  converter: ValueConverter,
  plans: SpecPlans,
  asDoubles: (value: unknown) => unknown,
): ValueConverter {
  if (plans.treeNumbers === "parsed") {
    return converter;
  }

  return {
    serialize: converter.serialize,
    deserialize: (value) => converter.deserialize(asDoubles(value)),
  };
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
  plans: SpecPlans,
  readOptions: JsonReadOptions | undefined,
  errorTypeName: ErrorTypeName = declaredTypeName,
): ValueConverter {
  switch (descriptor.kind) {
    case "primitive":
      return primitiveConverterOf(descriptor, readOptions, errorTypeName);
    case "int64":
      return int64ConverterOf(descriptor, readOptions, errorTypeName);
    case "temporal":
      return temporalConverterOf(descriptor, errorTypeName);
    case "decimal":
      return decimalConverterOf(descriptor, readOptions);
    case "bytes":
      return byteArrayConverter;
    case "array":
      return arrayConverter(descriptor, plans, readOptions);
    case "hashSet":
    case "list":
    case "queue":
    case "stack":
    case "immutableList":
    case "immutableArray":
    case "readOnlyList":
    case "immutableHashSet":
    case "sortedSet":
      return sequenceConverter(descriptor, descriptor.create, plans, readOptions);
    case "dictionary":
    case "immutableDictionary":
    case "concurrentDictionary":
    case "sortedDictionary":
    case "sortedList":
      return keyedConverter(descriptor, plans, readOptions);
    case "branded":
      return brandedConverter(descriptor);
    case "enum":
      return enumConverter(descriptor);
    case "numericEnum":
      return numericEnumConverter(descriptor);
    case "nullable":
      return nullableConverter(descriptor, plans, readOptions, errorTypeName);
    case "ref":
      return refConverter(descriptor, plans);
  }
}

// ─── Temporal ────────────────────────────────────────────────────────────────
// Reads go through `temporal-reads.ts` (#485); writes keep `temporal-text.ts`.

/** A custom converter of the key's kind, else the System.Text.Json key parser (#479). */
function keyConverterOf(key: TypeDescriptor, plans: SpecPlans): ValueConverter {
  return customOrBuiltinConverter(key, plans, () =>
    dictionaryKeyConverterOf(key, () => getBuiltinConverter(key, plans, undefined)),
  );
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
 * `JsonException` (#425); `null` reads as `null` for a reference or nullable
 * element and throws naming the element type for a value type (#471).
 */
function deserializeArray(
  value: unknown,
  elemConv: ValueConverter,
  element: TypeDescriptor,
): unknown[] | null {
  if (value == null) return null;

  if (!Array.isArray(value)) {
    throw unexpectedJsonKind("array", value);
  }

  const nullElementName = valueTypeNameOf(element);

  return readContainer("array", () =>
    value.map((item) => {
      if (item === null && nullElementName !== undefined) {
        throw cannotConvertToTypeName(nullElementName);
      }

      return elemConv.deserialize(item);
    }),
  );
}

function arrayConverter(
  descriptor: ArrayDescriptor,
  plans: SpecPlans,
  readOptions: JsonReadOptions | undefined,
) {
  const elemConv = resolveConverter(descriptor.element, plans, readOptions);

  return {
    serialize: (value: unknown) => serializeIterable(value, elemConv),
    deserialize: (value: unknown) => deserializeArray(value, elemConv, descriptor.element),
  };
}

// ─── Runtime collections (spec 011 FR-028) ───────────────────────────────────

/**
 * A collection written as a JSON array: written by iteration (a `Queue` front to
 * back, a `Stack` top first), read element by element and built by `create`.
 */
function sequenceConverter(
  descriptor: { readonly element: TypeDescriptor },
  create: (items: unknown[]) => Iterable<unknown>,
  plans: SpecPlans,
  readOptions: JsonReadOptions | undefined,
) {
  const elemConv = resolveConverter(descriptor.element, plans, readOptions);

  return {
    serialize: (value: unknown) => serializeIterable(value, elemConv),
    deserialize: (value: unknown) => {
      const items = deserializeArray(value, elemConv, descriptor.element);

      return items === null ? null : create(items);
    },
  };
}

/**
 * A dictionary written as a JSON object with System.Text.Json's key rule (#479): written
 * by iterating its `[key, value]` entries, read into entries in document order and
 * built by `create`.
 */
function keyedConverter(
  descriptor: KeyedCollectionDescriptor,
  plans: SpecPlans,
  readOptions: JsonReadOptions | undefined,
) {
  const keyConv = keyConverterOf(descriptor.key, plans);
  const valueConv = resolveConverter(descriptor.value, plans, readOptions);

  return {
    serialize: (value: unknown) => {
      if (value == null) return null;

      return writeContainer(() => {
        const result: Record<string, unknown> = {};

        for (const [k, v] of value as Iterable<readonly [unknown, unknown]>) {
          result[String(keyConv.serialize(k))] = valueConv.serialize(v);
        }

        return result;
      });
    },
    deserialize: (value: unknown) => {
      if (value == null) return null;

      if (typeof value !== "object" || Array.isArray(value)) {
        throw unexpectedJsonKind("object", value);
      }

      const nullValueName = valueTypeNameOf(descriptor.value);

      return readContainer("object", () => {
        const entries: Array<readonly [unknown, unknown]> = [];

        for (const [k, v] of Object.entries(value)) {
          if (v === null && nullValueName !== undefined) {
            throw cannotConvertToTypeName(nullValueName);
          }

          entries.push([keyConv.deserialize(k), valueConv.deserialize(v)]);
        }

        return descriptor.create(entries);
      });
    },
  };
}

// ─── Branded ─────────────────────────────────────────────────────────────────

function brandedConverter(descriptor: BrandedDescriptor) {
  return {
    serialize: exactBigIntLeafOf, // brand erases at runtime; a bigint writes its digits (FR-056)
    // A branded value is a value type: JSON `null` throws, as System.Text.Json
    // throws (#471).
    deserialize: (value: unknown) => {
      if (value == null) {
        throw cannotConvertToTypeName(descriptor.typeName);
      }

      return descriptor.create(value);
    },
  };
}

// ─── Enum (String) ───────────────────────────────────────────────────────────

function enumConverter(descriptor: EnumDescriptor) {
  const validValues = new Set(Object.values(descriptor.values));

  return {
    serialize: (value: unknown) => value, // already a string
    deserialize: (value: unknown) => {
      // An enum is a value type: JSON `null` throws its conversion error,
      // as System.Text.Json throws (#471), before the enum-value error.
      if (value === null) {
        throw cannotConvertToTypeName(descriptor.typeName);
      }

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

/**
 * A numeric enum reads any integer of its underlying type, whether a member
 * declares it or not, as System.Text.Json does (#369): a `[Flags]`
 * combination (`3` for `Read | Write`) and an undeclared value (`42`) both
 * read. Any other JSON value, an integer outside the underlying type
 * included, throws System.Text.Json's conversion error naming the enum.
 */
function numericEnumConverter(descriptor: NumericEnumDescriptor) {
  const reader = holdsBigInts(descriptor) ? int64EnumReader(descriptor) : undefined;
  const numberType = descriptor.numberType ?? DEFAULT_ENUM_INTEGER_TYPE;

  return {
    // A 32-bit enum value is already a number.
    serialize: (value: unknown) => (reader ? reader.serialize(value) : value),
    deserialize: (value: unknown) => {
      // A value type: JSON `null` throws its conversion error (#471).
      if (value === null) {
        throw cannotConvertToTypeName(descriptor.typeName);
      }

      if (reader) {
        return reader.deserialize(value);
      }

      if (!isIntegerOfType(value, numberType)) {
        throw cannotConvertToTypeName(descriptor.typeName);
      }

      return value;
    },
  };
}

/** Whether `value` is a `number` holding an integer within `numberType`'s range. */
function isIntegerOfType(value: unknown, numberType: IntegerTypeName): value is number {
  return (
    typeof value === "number" &&
    Number.isInteger(value) &&
    isWithinIntegerRange(BigInt(value), numberType)
  );
}

/**
 * A 64-bit enum's values are `bigint`s (#701): they are written as their
 * exact digits and read from the exact token, in the underlying type's
 * range, through the `long` / `ulong` converter, whose errors name the enum.
 */
function int64EnumReader(descriptor: Int64EnumDescriptor): ValueConverter {
  const { numberType, typeName = numberType } = descriptor;

  return int64ConverterOf({ kind: "int64", numberType }, undefined, () => typeName);
}

// ─── Nullable ────────────────────────────────────────────────────────────────

function nullableConverter(
  descriptor: NullableDescriptor,
  plans: SpecPlans,
  readOptions: JsonReadOptions | undefined,
  errorTypeName: ErrorTypeName = declaredTypeName,
) {
  const innerConv = nullableInnerConverter(descriptor.inner, plans, readOptions, errorTypeName);

  return {
    serialize: (value: unknown) => (value == null ? null : innerConv.serialize(value)),
    deserialize: (value: unknown) => (value == null ? null : innerConv.deserialize(value)),
  };
}

/** A nullable's inner converter, resolved as `resolveConverter` does. */
function nullableInnerConverter(
  inner: TypeDescriptor,
  plans: SpecPlans,
  readOptions: JsonReadOptions | undefined,
  errorTypeName: ErrorTypeName,
): ValueConverter {
  return customOrBuiltinConverter(inner, plans, () =>
    builtinNullableInnerConverter(inner, plans, readOptions, errorTypeName),
  );
}

/**
 * The built-in converter of a nullable's inner type: a numeric one names
 * ``System.Nullable`1[<CLR name>]`` in its errors, as System.Text.Json does;
 * the rest name the type of their position (`errorTypeName`).
 */
function builtinNullableInnerConverter(
  inner: TypeDescriptor,
  plans: SpecPlans,
  readOptions: JsonReadOptions | undefined,
  errorTypeName: ErrorTypeName,
): ValueConverter {
  switch (inner.kind) {
    case "primitive":
      return primitiveConverterOf(inner, readOptions, nullableTypeName);
    case "int64":
      return int64ConverterOf(inner, readOptions, nullableTypeName);
    case "decimal":
      return decimalConverterOf(inner, readOptions, nullableTypeName);
    default:
      return getBuiltinConverter(inner, plans, readOptions, errorTypeName);
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
function refConverter(descriptor: RefDescriptor, plans: SpecPlans) {
  const targetSpec = lazy(() => descriptor.spec());
  const targetPlan = lazy(() => plans.planOf(targetSpec()));

  return {
    serialize: (value: unknown) => {
      if (value == null) return null;
      return targetPlan().serialize(value);
    },
    // JSON `null` for a struct contract (a property, an element or a
    // dictionary value) throws as in System.Text.Json; a `Nullable<T>`
    // wraps the ref in a `nullable` descriptor, which reads it first.
    deserialize: (value: unknown) => {
      if (value == null) return readNull(targetSpec());
      return targetPlan().deserialize(value);
    },
  };
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
