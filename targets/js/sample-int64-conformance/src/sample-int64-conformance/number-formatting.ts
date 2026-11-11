import { alignValue, formatDouble, formatInteger } from "metano-runtime";

export function interpolateDouble(formatted: number): string {
  return `[${formatDouble(formatted, "F2", 12)}][${formatDouble(formatted, "N0")}][${formatDouble(formatted, "E3", -14)}][${formatDouble(formatted, "P1")}][${formatDouble(formatted, "G5")}][${formatDouble(formatted, "C")}][${formatDouble(formatted, "R")}][${formatDouble(formatted, "g17")}][${formatDouble(formatted, "F0")}][${formatDouble(formatted, "e")}]`;
}

export function interpolateNullableDouble(formatted: number | null): string {
  return `[${formatDouble(formatted, "F1", 8)}][${formatDouble(formatted, "N")}]`;
}

export function interpolateInt32(formatted: number): string {
  return `[${alignValue(formatted, 8)}][${formatInteger(formatted, 32, "D5")}][${formatInteger(formatted, 32, "X")}][${formatInteger(formatted, 32, "N0", -16)}][${formatInteger(formatted, 32, "E2")}][${formatInteger(formatted, 32, "P0")}][${formatInteger(formatted, 32, "C")}][${formatInteger(formatted, 32, "G2")}][${formatInteger(formatted, 32, "x4")}]`;
}

export function interpolateNullableInt32(formatted: number | null): string {
  return `[${alignValue(formatted, 4)}][${formatInteger(formatted, 32, "D2")}]`;
}

export function interpolateUInt32(formatted: number): string {
  return `[${formatInteger(formatted, 32, "X")}][${formatInteger(formatted, 32, "N0")}]`;
}

export function interpolateInt16(formatted: number): string {
  return `[${formatInteger(formatted, 16, "X")}][${formatInteger(formatted, 16, "D6", 7)}]`;
}

export function interpolateUInt16(formatted: number): string {
  return `[${formatInteger(formatted, 16, "x")}][${formatInteger(formatted, 16, "E1")}]`;
}

export function interpolateSByte(formatted: number): string {
  return `[${formatInteger(formatted, 8, "X2")}][${formatInteger(formatted, 8, "D4")}]`;
}

export function interpolateByte(formatted: number): string {
  return `[${formatInteger(formatted, 8, "x")}][${formatInteger(formatted, 8, "P0", 9)}]`;
}

export function formatDoubleConstant(formatted: number): string {
  return formatDouble(formatted, "N3") + "|" + formatDouble(formatted, "E");
}

export function formatInt32Constant(formatted: number): string {
  return formatInteger(formatted, 32, "D3") + "|" + formatInteger(formatted, 32, "X8");
}
