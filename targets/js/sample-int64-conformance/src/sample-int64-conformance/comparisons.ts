import { compareInt64, objectEquals, valueHashCode } from "metano-runtime";

export function compareTo(value: bigint, other: bigint): number {
  return compareInt64(value, other);
}

export function compareToUnsigned(value: bigint, other: bigint): number {
  return compareInt64(value, other);
}

export function equalsLong(value: bigint, other: bigint): boolean {
  return (value === other);
}

export function equalsBoxed(value: bigint, boxed: Object): boolean {
  return objectEquals(value, boxed);
}

export function getHashCodeOf(value: bigint): number {
  return valueHashCode(value);
}
