import { parseInt64, parseUInt64, tryParseInt64Value, tryParseUInt64 } from "metano-runtime";

export function parse(text: string): bigint {
  return parseInt64(text);
}

export function parseHex(text: string): bigint {
  return parseInt64(text, 515);
}

export function parseThousands(text: string): bigint {
  return parseInt64(text, 71);
}

export function parseNumber(text: string): bigint {
  return parseInt64(text, 111);
}

export function parseUnsigned(text: string): bigint {
  return parseUInt64(text);
}

export function parseUnsignedHex(text: string): bigint {
  return parseUInt64(text, 515);
}

export function parseUnsignedNumber(text: string): bigint {
  return parseUInt64(text, 111);
}

export function tryParse(text: string): bigint | null {
  const [$found0, value] = tryParseInt64Value(text);

  return $found0 ? value : null;
}

export function tryParseUnsignedNumber(text: string): bigint | null {
  const [$found0, value] = tryParseUInt64(text, 111);

  return $found0 ? value : null;
}

export function tryParseFailureValue(text: string): bigint {
  const [, value] = tryParseInt64Value(text, 515);

  return value;
}
