import { alignValue, formatDouble as formatDouble$rt, formatInteger } from "metano-runtime";

export function interpolateDouble(formatted: number): string {
  return `[${formatDouble$rt(formatted, "F2", 12)}][${formatDouble$rt(formatted, "N0")}][${formatDouble$rt(formatted, "E3", -14)}][${formatDouble$rt(formatted, "P1")}][${formatDouble$rt(formatted, "G5")}][${formatDouble$rt(formatted, "C")}][${formatDouble$rt(formatted, "R")}][${formatDouble$rt(formatted, "g17")}][${formatDouble$rt(formatted, "F0")}][${formatDouble$rt(formatted, "e")}]`;
}

export function interpolateNullableDouble(formatted: number | null): string {
  return `[${formatDouble$rt(formatted, "F1", 8)}][${formatDouble$rt(formatted, "N")}]`;
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
  return formatDouble$rt(formatted, "N3") + "|" + formatDouble$rt(formatted, "E");
}

export function formatInt32Constant(formatted: number): string {
  return formatInteger(formatted, 32, "D3") + "|" + formatInteger(formatted, 32, "X8");
}

export function formatDouble(formatted: number, numberFormat: string): string {
  return formatDouble$rt(formatted, numberFormat);
}

export function formatInt32(formatted: number, numberFormat: string): string {
  return formatInteger(formatted, 32, numberFormat);
}

export function formatUInt32(formatted: number, numberFormat: string): string {
  return formatInteger(formatted, 32, numberFormat);
}

export function formatInt16(formatted: number, numberFormat: string): string {
  return formatInteger(formatted, 16, numberFormat);
}

export function formatUInt16(formatted: number, numberFormat: string): string {
  return formatInteger(formatted, 16, numberFormat);
}

export function formatSByte(formatted: number, numberFormat: string): string {
  return formatInteger(formatted, 8, numberFormat);
}

export function formatByte(formatted: number, numberFormat: string): string {
  return formatInteger(formatted, 8, numberFormat);
}
