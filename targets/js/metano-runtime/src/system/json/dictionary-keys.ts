import type {
  DecimalDescriptor,
  EnumDescriptor,
  NumberTypeName,
  NumericEnumDescriptor,
  PrimitiveDescriptor,
  TemporalDescriptor,
  TypeDescriptor,
} from "./types.ts";
import type { ValueConverter } from "./value-converter.ts";
import { readDecimalText } from "./decimal-text.ts";
import { JsonException } from "./errors.ts";
import { readNumberText } from "./number-from-string.ts";
import { ordinalIgnoreCaseKeyOf } from "./ordinal-ignore-case.ts";
import { temporalTextOf } from "./temporal-text.ts";

const BOOLEAN_TYPE_NAME = "System.Boolean";

const GUID_TYPE_NAME = "System.Guid";

const DECIMAL_TYPE_NAME = "System.Decimal";

/** System.Text.Json names a type without a CLR name `Object` (`contract-name.ts`). */
const UNNAMED_TYPE_NAME = "Object";

const BOOLEAN_KEYS: ReadonlyMap<string, boolean> = new Map([
  [ordinalIgnoreCaseKeyOf("true"), true],
  [ordinalIgnoreCaseKeyOf("false"), false],
]);

/** `Guid` property names are read in the `D` format only (`8-4-4-4-12`). */
const GUID_D_FORMAT = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** `Enum.TryParse` takes integer text when the name starts like a number. */
const ENUM_INTEGER_PATTERN = /^[+-]?[0-9]+$/;

/**
 * The range of an enum read from integer text. The descriptor does not name
 * the underlying type, so every enum is checked as an `int`, the C# default.
 */
const ENUM_INTEGER_TYPE: NumberTypeName = "System.Int32";

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
 *   them (OR-ed), or an integer, with surrounding whitespace ignored;
 *   written as the member name, or as the number when no member has it;
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
 * keep the converter of their value descriptor (`valueConverterOf`).
 *
 * TODO(012e): a `long` / `ulong` key reads as a `number` while the bigint
 * staging switch is in Number mode (spec 012 FR-065); slice 012e reads it
 * as a `bigint`.
 */
export function dictionaryKeyConverterOf(
  descriptor: TypeDescriptor,
  valueConverterOf: () => ValueConverter,
): ValueConverter {
  switch (descriptor.kind) {
    case "primitive":
      return primitiveKeyConverterOf(descriptor) ?? valueConverterOf();
    case "numericEnum":
      return numericEnumKeyConverter(descriptor);
    case "enum":
      return stringEnumKeyConverter(descriptor);
    case "branded":
      return descriptor.typeName === GUID_TYPE_NAME ? guidKeyConverter : valueConverterOf();
    case "decimal":
      return decimalKeyConverter(descriptor);
    case "temporal":
      return temporalKeyConverter(descriptor);
    default:
      return valueConverterOf();
  }
}

/** A number written as `String()` writes it, read by its type's grammar. */
function primitiveKeyConverterOf(descriptor: PrimitiveDescriptor): ValueConverter | undefined {
  const { numberType } = descriptor;

  if (numberType !== undefined) {
    return keyConverter(String, (name) => readNumberText(name, numberType), numberType);
  }

  return descriptor.typeName === BOOLEAN_TYPE_NAME ? booleanKeyConverter : undefined;
}

function numericEnumKeyConverter(descriptor: NumericEnumDescriptor): ValueConverter {
  const members = Object.entries(descriptor.values).filter(
    (member): member is [string, number] => typeof member[1] === "number",
  );

  const nameOf = new Map(members.map(([name, value]) => [value, name]));
  const valueOfName = memberLookupOf(members);

  /** A comma-separated list of names, OR-ed as `Enum.TryParse` does. */
  const readNames = (text: string): number | undefined => {
    let combined = 0;

    for (const name of text.split(ENUM_NAME_SEPARATOR)) {
      const value = valueOfName(name.trim());

      if (value === undefined) {
        return undefined;
      }

      combined |= value;
    }

    return combined;
  };

  return keyConverter(
    (value) => nameOf.get(value as number) ?? String(value),
    (name) => {
      const text = name.trim();

      return ENUM_INTEGER_PATTERN.test(text)
        ? readNumberText(text, ENUM_INTEGER_TYPE)
        : readNames(text);
    },
    descriptor.typeName,
  );
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
    (name) => parsedOrUndefined(descriptor.parse, name),
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
      const value = read(name as string);

      if (value === undefined) {
        throw cannotConvertKey(typeName);
      }

      return value;
    },
  };
}

function cannotConvertKey(typeName: string | undefined): JsonException {
  return new JsonException(
    `The JSON value could not be converted to ${typeName ?? UNNAMED_TYPE_NAME}.`,
  );
}
