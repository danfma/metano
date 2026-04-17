/**
 * Specialized runtime type checks for C# numeric and string types.
 * Used by Metano-generated overload dispatchers to differentiate
 * types that map to the same JS primitive (e.g., int vs long → both number).
 */

// ─── String types ───────────────────────────────────────

/** C# `char` → single-character string */
export function isChar(value: unknown): value is string {
  return typeof value === "string" && value.length === 1;
}

/** C# `string` */
export function isString(value: unknown): value is string {
  return typeof value === "string";
}

// ─── Integer types ──────────────────────────────────────

/** C# `byte` (0..255) */
export function isByte(value: unknown): value is number {
  return typeof value === "number" && Number.isInteger(value) && value >= 0 && value <= 255;
}

/** C# `sbyte` (-128..127) */
export function isSByte(value: unknown): value is number {
  return typeof value === "number" && Number.isInteger(value) && value >= -128 && value <= 127;
}

/** C# `short` / `Int16` (-32768..32767) */
export function isInt16(value: unknown): value is number {
  return typeof value === "number" && Number.isInteger(value) && value >= -32768 && value <= 32767;
}

/** C# `ushort` / `UInt16` (0..65535) */
export function isUInt16(value: unknown): value is number {
  return typeof value === "number" && Number.isInteger(value) && value >= 0 && value <= 65535;
}

/** C# `int` / `Int32` (-2147483648..2147483647) */
export function isInt32(value: unknown): value is number {
  return (
    typeof value === "number" &&
    Number.isInteger(value) &&
    value >= -2147483648 &&
    value <= 2147483647
  );
}

/** C# `uint` / `UInt32` (0..4294967295) */
export function isUInt32(value: unknown): value is number {
  return typeof value === "number" && Number.isInteger(value) && value >= 0 && value <= 4294967295;
}

/** C# `long` / `Int64` (safe integer range) */
export function isInt64(value: unknown): value is number {
  return (
    typeof value === "number" &&
    Number.isInteger(value) &&
    value >= Number.MIN_SAFE_INTEGER &&
    value <= Number.MAX_SAFE_INTEGER
  );
}

/** C# `ulong` / `UInt64` (0..safe integer range) */
export function isUInt64(value: unknown): value is number {
  return (
    typeof value === "number" &&
    Number.isInteger(value) &&
    value >= 0 &&
    value <= Number.MAX_SAFE_INTEGER
  );
}

// ─── Floating-point types ───────────────────────────────

/** C# `float` / `Single` */
export function isFloat32(value: unknown): value is number {
  return typeof value === "number";
}

/** C# `double` / `Double` */
export function isFloat64(value: unknown): value is number {
  return typeof value === "number";
}

// ─── Other primitive types ──────────────────────────────

/** C# `bool` */
export function isBool(value: unknown): value is boolean {
  return typeof value === "boolean";
}

/** C# `BigInteger` */
export function isBigInt(value: unknown): value is bigint {
  return typeof value === "bigint";
}
