import { formatInt64 } from "metano-runtime";

export function format(value: bigint, format: string): string {
  return formatInt64(value, format);
}

export function formatUnsigned(value: bigint, format: string): string {
  return formatInt64(value, format);
}

export function interpolate(value: bigint): string {
  return `[${formatInt64(value, null, 8)}][${formatInt64(value, null, -8)}][${formatInt64(value, "N0")}][${formatInt64(value, "X", 24)}][${formatInt64(value, "D3")}][${value}]`;
}

export function interpolateUnsigned(value: bigint): string {
  return `[${formatInt64(value, "N0", 25)}][${formatInt64(value, "x")}]`;
}
