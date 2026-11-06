import type {
  DecimalDescriptor,
  EnumDescriptor,
  Int64Descriptor,
  IntegerTypeName,
  NumericEnumDescriptor,
  PrimitiveDescriptor,
  TemporalDescriptor,
  TypeDescriptor,
} from "./types.ts";
import type { ValueConverter } from "./value-converter.ts";
import { readDecimalText } from "./decimal-text.ts";
import { cannotConvertToTypeName } from "./contract-name.ts";
import { readEnumIntegerText, readNumberText, readNumberValue } from "./number-from-string.ts";
import { ordinalIgnoreCaseKeyOf } from "./ordinal-ignore-case.ts";
import { temporalTextOf } from "./temporal-text.ts";
import { readTemporalText } from "./temporal-reads.ts";

const BOOLEAN_TYPE_NAME = "System.Boolean";

const GUID_TYPE_NAME = "System.Guid";

const DECIMAL_TYPE_NAME = "System.Decimal";

const BOOLEAN_KEYS: ReadonlyMap<string, boolean> = new Map([
  [ordinalIgnoreCaseKeyOf("true"), true],
  [ordinalIgnoreCaseKeyOf("false"), false],
]);

/** `Guid` property names are read in the `D` format only (`8-4-4-4-12`). */
const GUID_D_FORMAT = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** The underlying type of an enum key whose descriptor names none, the C# default. */
const DEFAULT_ENUM_INTEGER_TYPE: IntegerTypeName = "System.Int32";

const ENUM_NAME_SEPARATOR = ",";

const booleanKeyConverter = keyConverter(
  (value) => (value ? "True" : "False"),
  (name) => BOOLEAN_KEYS.get(ordinalIgnoreCaseKeyOf(name)),
  BOOLEAN_TYPE_NAME,
);

const guidKeyConverter = keyConverter(
  String,
  (name) => (GUID_D_FORMAT.test(name) ? name.toLowerCase() : undefined),
  GUID_TYPE_NAME,
);

/**
 * Dictionary keys (#479). A JSON property name is always a string, so each
 * key descriptor reads it into the key's C# type the way System.Text.Json
 * does (measured on .NET 10), whatever the context's read options:
 *
 * - a number (`numberType`): the grammar of numbers read from strings
 *   (`number-from-string.ts`), so `"+5"` and `"007"` read as `5` and `7`
 *   for `int`, and `"x"` throws;
 * - a `bool` (`typeName: "System.Boolean"`): `true` / `false` in any case,
 *   written `True` / `False`;
 * - a numeric enum: a member name in any case, a comma-separated list of
 *   them (OR-ed), or an integer with an optional sign within the underlying
 *   `numberType`, with surrounding whitespace ignored; written as the member
 *   name (an aliased value by its first member), or as the number when no
 *   member has it;
 * - a `[StringEnum]`: a member value in any case, with surrounding
 *   whitespace ignored;
 * - a `Guid` (`typeName: "System.Guid"`): the `D` format in any case, read
 *   as lowercase;
 * - a `decimal`: the decimal grammar (`decimal-text.ts`), written with
 *   `toFixed()`;
 * - a temporal type: its `parse`, written as its value is.
 *
 * A name that does not parse throws `JsonException` `The JSON value could
 * not be converted to <typeName>.`. A `string` key, a key without a type
 * (a hand-written `{ kind: "primitive" }`) and any other descriptor kind
 * keep the converter of their value descriptor (`builtinConverterOf`).
 */
export function dictionaryKeyConverterOf(
  descriptor: TypeDescriptor,
  builtinConverterOf: () => ValueConverter,
): ValueConverter {
  switch (descriptor.kind) {
    case "primitive":
      return primitiveKeyConverterOf(descriptor) ?? builtinConverterOf();
    case "int64":
      return int64KeyConverter(descriptor);
    case "numericEnum":
      return numericEnumKeyConverter(descriptor);
    case "enum":
      return stringEnumKeyConverter(descriptor);
    case "branded":
      return descriptor.typeName === GUID_TYPE_NAME ? guidKeyConverter : builtinConverterOf();
    case "decimal":
      return decimalKeyConverter(descriptor);
    case "temporal":
      return temporalKeyConverter(descriptor);
    default:
      return builtinConverterOf();
  }
}

/** A number written as `String()` writes it, read by its type's grammar. */
function primitiveKeyConverterOf(descriptor: PrimitiveDescriptor): ValueConverter | undefined {
  const { numberType } = descriptor;

  if (numberType !== undefined) {
    return keyConverter(String, (name) => readNumberValue(name, numberType), numberType);
  }

  return descriptor.typeName === BOOLEAN_TYPE_NAME ? booleanKeyConverter : undefined;
}

/**
 * A 64-bit key (spec 012 FR-055): written as `key.toString()`, read with the
 * FR-048 integer grammar into the `bigint` in range — `"+5"` and `"05"`
 * read for a signed key, `"05"` alone for an unsigned one, whatever the
 * read options, because System.Text.Json always reads such a key from the
 * property name (measured: `"-0"` reads `0` for a `long` key and throws for
 * a `ulong` key).
 */
function int64KeyConverter(descriptor: Int64Descriptor): ValueConverter {
  const { numberType } = descriptor;

  return keyConverter(String, (name) => readNumberText(name, numberType), numberType);
}

function numericEnumKeyConverter(descriptor: NumericEnumDescriptor): ValueConverter {
  const members = Object.entries(descriptor.values).filter(
    (member): member is [string, number] => typeof member[1] === "number",
  );

  // An aliased value is written under its first member, as .NET names it.
  const nameOfValue = new Map(members.toReversed().map(([name, value]) => [value, name]));
  const valueOfName = memberLookupOf(members);
  const integerType = descriptor.numberType ?? DEFAULT_ENUM_INTEGER_TYPE;

  return keyConverter(
    // A numeric enum value is a number: a member's, or one no member declares.
    (value) => nameOfValue.get(value as number) ?? String(value),
    (name) => readNumericEnumKey(name.trim(), integerType, valueOfName),
    descriptor.typeName,
  );
}

/**
 * An integer within the underlying type (an optional sign whatever the
 * type), or a comma-separated list of member names, as `Enum.TryParse`
 * reads them.
 */
function readNumericEnumKey(
  text: string,
  integerType: IntegerTypeName,
  valueOfName: (name: string) => number | undefined,
): number | undefined {
  // No member name spells an integer, so the name list never reads one.
  return readEnumIntegerText(text, integerType) ?? readEnumNameList(text, valueOfName);
}

/**
 * The member names of `text`, OR-ed as 64-bit integers so a `uint` or `long`
 * member past 2^31 keeps its value; `undefined` when one is not a member.
 */
function readEnumNameList(
  text: string,
  valueOfName: (name: string) => number | undefined,
): number | undefined {
  const values = text.split(ENUM_NAME_SEPARATOR).map((name) => valueOfName(name.trim()));

  if (!values.every((value): value is number => value !== undefined)) {
    return undefined;
  }

  return values.length === 1
    ? values[0]
    : Number(values.reduce((combined, value) => combined | BigInt(value), 0n));
}

function stringEnumKeyConverter(descriptor: EnumDescriptor): ValueConverter {
  const valueOfName = memberLookupOf(
    Object.values(descriptor.values).map((value) => [value, value] as const),
  );

  return keyConverter(String, (name) => valueOfName(name.trim()), descriptor.typeName);
}

/**
 * The value of a member name: the exact name first, else the first member
 * whose name matches it ignoring case (`StringComparer.OrdinalIgnoreCase`).
 */
function memberLookupOf<T>(
  members: ReadonlyArray<readonly [name: string, value: T]>,
): (name: string) => T | undefined {
  const exact = new Map(members);
  const ignoringCase = new Map<string, T>();

  for (const [name, value] of members) {
    const key = ordinalIgnoreCaseKeyOf(name);

    if (!ignoringCase.has(key)) {
      ignoringCase.set(key, value);
    }
  }

  return (name) => exact.get(name) ?? ignoringCase.get(ordinalIgnoreCaseKeyOf(name));
}

/** decimal.js `Decimal` when the descriptor has its `type`, else the text read. */
function decimalKeyConverter(descriptor: DecimalDescriptor): ValueConverter {
  const { type } = descriptor;

  return keyConverter(
    decimalKeyTextOf,
    (name) => {
      const text = readDecimalText(name);

      return text !== undefined && type ? new type(text) : text;
    },
    DECIMAL_TYPE_NAME,
  );
}

/** The plain digits of a decimal.js value (`toFixed()`), else its `String()`. */
function decimalKeyTextOf(value: unknown): string {
  const fixed = (value as { toFixed?: () => string } | null)?.toFixed;

  return typeof value === "object" && typeof fixed === "function"
    ? fixed.call(value)
    : String(value);
}

function temporalKeyConverter(descriptor: TemporalDescriptor): ValueConverter {
  return keyConverter(
    temporalTextOf,
    (name) => parsedOrUndefined((text) => readTemporalText(descriptor, text), name),
    descriptor.typeName,
  );
}

/** `parse(text)`, or `undefined` when it throws, so the key reports System.Text.Json's error. */
function parsedOrUndefined(parse: (text: string) => unknown, text: string): unknown {
  try {
    return parse(text);
  } catch {
    return undefined;
  }
}

/**
 * A key converter that writes with `write` and reads with `read`, which
 * answers `undefined` for a name the key type rejects.
 */
function keyConverter(
  write: (value: unknown) => string,
  read: (name: string) => unknown,
  typeName: string | undefined,
): ValueConverter {
  return {
    serialize: write,
    deserialize: (name) => {
      // A JSON property name is always a string.
      const value = read(String(name));

      if (value === undefined) {
        throw cannotConvertToTypeName(typeName);
      }

      return value;
    },
  };
}
