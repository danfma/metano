import type { PrimitiveType } from "./runtime-type.ts";
import {
  isBigInt,
  isBool,
  isByte,
  isChar,
  isFloat32,
  isFloat64,
  isInt16,
  isInt32,
  isInt64,
  isSByte,
  isString,
  isUInt16,
  isUInt32,
  isUInt64,
} from "../../type-checking/index.ts";
import { toStringTag } from "./to-string-tag.ts";

// Plain data literals: they reference the validating checks, never call them at load time.
// C# `long`, `ulong` and `BigInteger` are JS `bigint` (spec 012), so their checks take a bigint.

export const BooleanType: PrimitiveType<boolean> = {
  kind: "primitive",
  name: "Boolean",
  fullName: "System.Boolean",
  namespace: "System",
  primitive: "Boolean",
  test: isBool,
  default: () => false,
};

export const ByteType: PrimitiveType<number> = {
  kind: "primitive",
  name: "Byte",
  fullName: "System.Byte",
  namespace: "System",
  primitive: "Byte",
  test: isByte,
  default: () => 0,
};

export const SByteType: PrimitiveType<number> = {
  kind: "primitive",
  name: "SByte",
  fullName: "System.SByte",
  namespace: "System",
  primitive: "SByte",
  test: isSByte,
  default: () => 0,
};

export const Int16Type: PrimitiveType<number> = {
  kind: "primitive",
  name: "Int16",
  fullName: "System.Int16",
  namespace: "System",
  primitive: "Int16",
  test: isInt16,
  default: () => 0,
};

export const UInt16Type: PrimitiveType<number> = {
  kind: "primitive",
  name: "UInt16",
  fullName: "System.UInt16",
  namespace: "System",
  primitive: "UInt16",
  test: isUInt16,
  default: () => 0,
};

export const Int32Type: PrimitiveType<number> = {
  kind: "primitive",
  name: "Int32",
  fullName: "System.Int32",
  namespace: "System",
  primitive: "Int32",
  test: isInt32,
  default: () => 0,
};

export const UInt32Type: PrimitiveType<number> = {
  kind: "primitive",
  name: "UInt32",
  fullName: "System.UInt32",
  namespace: "System",
  primitive: "UInt32",
  test: isUInt32,
  default: () => 0,
};

export const Int64Type: PrimitiveType<bigint> = {
  kind: "primitive",
  name: "Int64",
  fullName: "System.Int64",
  namespace: "System",
  primitive: "Int64",
  test: isInt64,
  default: () => 0n,
};

export const UInt64Type: PrimitiveType<bigint> = {
  kind: "primitive",
  name: "UInt64",
  fullName: "System.UInt64",
  namespace: "System",
  primitive: "UInt64",
  test: isUInt64,
  default: () => 0n,
};

export const SingleType: PrimitiveType<number> = {
  kind: "primitive",
  name: "Single",
  fullName: "System.Single",
  namespace: "System",
  primitive: "Single",
  test: isFloat32,
  default: () => 0,
};

export const DoubleType: PrimitiveType<number> = {
  kind: "primitive",
  name: "Double",
  fullName: "System.Double",
  namespace: "System",
  primitive: "Double",
  test: isFloat64,
  default: () => 0,
};

export const BigIntegerType: PrimitiveType<bigint> = {
  kind: "primitive",
  name: "BigInteger",
  fullName: "System.BigInteger",
  namespace: "System",
  primitive: "BigInteger",
  test: isBigInt,
  default: () => 0n,
};

export const CharType: PrimitiveType<string> = {
  kind: "primitive",
  name: "Char",
  fullName: "System.Char",
  namespace: "System",
  primitive: "Char",
  test: isChar,
  default: () => "\0",
};

export const StringType: PrimitiveType<string> = {
  kind: "primitive",
  name: "String",
  fullName: "System.String",
  namespace: "System",
  primitive: "String",
  test: isString,
  default: () => "",
};

export const GuidType: PrimitiveType<string> = {
  kind: "primitive",
  name: "Guid",
  fullName: "System.Guid",
  namespace: "System",
  primitive: "Guid",
  test: isString,
};

/** `decimal`: recognized by the decimal.js `Symbol.toStringTag`, so decimal.js is never imported. */
export const DecimalType: PrimitiveType<object> = {
  kind: "primitive",
  name: "Decimal",
  fullName: "System.Decimal",
  namespace: "System",
  primitive: "Decimal",
  test: (value: unknown): value is object => toStringTag(value) === "[object Decimal]",
};
