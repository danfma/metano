import { ArgumentException } from "../exceptions/argument-exception.ts";
import type { EnumType } from "./runtime-type.ts";

const INTEGER_TEXT = /^[+-]?\d+$/;

/**
 * `Enum.ToObject` / `Enum.Parse`, faithful to .NET. An integral number is accepted as is,
 * an undeclared value included, and never throws. A string is a declared member name
 * (case-sensitive unless `ignoreCase`; a comma-separated list is OR-ed together, as .NET does)
 * or a numeric string; only an unknown name throws. A `[StringEnum]` maps a name to its value.
 */
export function enumFromRaw<E>(type: EnumType<E>, raw: unknown, ignoreCase = false): E {
  if (typeof raw === "number" && Number.isInteger(raw)) {
    return raw as E;
  }

  if (typeof raw === "string") {
    return type.kind === "stringEnum"
      ? stringEnumValue(type, raw, ignoreCase)
      : numericEnumValue(type, raw, ignoreCase);
  }

  throw new ArgumentException("The value must be an integral number or a string.", "value");
}

function stringEnumValue<E>(type: EnumType<E>, text: string, ignoreCase: boolean): E {
  const index = indexOfName(type, text.trim(), ignoreCase);

  if (index < 0) {
    throw notFound(text);
  }

  return type.values[index] as E;
}

function numericEnumValue<E>(type: EnumType<E>, text: string, ignoreCase: boolean): E {
  if (text.trim() === "") {
    throw new ArgumentException("Must specify valid information for parsing in the string.");
  }

  return text
    .split(",")
    .reduce((combined, token) => combined | tokenValue(type, token.trim(), ignoreCase), 0) as E;
}

function tokenValue<E>(type: EnumType<E>, token: string, ignoreCase: boolean): number {
  if (INTEGER_TEXT.test(token)) {
    return Number.parseInt(token, 10);
  }

  const index = indexOfName(type, token, ignoreCase);

  if (index < 0) {
    throw notFound(token);
  }

  return type.values[index] as number;
}

function indexOfName<E>(type: EnumType<E>, name: string, ignoreCase: boolean): number {
  if (!ignoreCase) {
    return type.names.indexOf(name);
  }

  const lower = name.toLowerCase();

  return type.names.findIndex((candidate) => candidate.toLowerCase() === lower);
}

function notFound(name: string): ArgumentException {
  return new ArgumentException(`Requested value '${name}' was not found.`);
}
