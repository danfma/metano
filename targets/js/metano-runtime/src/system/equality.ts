/**
 * Deep value-equality and standalone hashing helpers for arbitrary JavaScript values.
 * Mirror the C# .NET semantics: structural equality for compounds, NaN-equals-NaN for
 * floats, custom `equals(other)` / `hashCode()` methods on user types take precedence.
 *
 * The compiler also calls into {@link valueEquals} from synthesized record
 * `equals` methods whenever a constructor parameter has a non-primitive type that
 * exposes a value-equality contract (Decimal, Temporal types, nested records, …).
 * The previous lowering used `===` for every field which silently diverged from C#
 * semantics for value-wrapper types — see #202. It also lowers C# `==` / `!=` over
 * records to {@link valueEquals}, and every C# `GetHashCode()` call to
 * {@link valueHashCode} (#254).
 */
import { HashCode } from "./hash-code.ts";
import { hashInt64 } from "./int64.ts";

/**
 * Returns true when `a` and `b` are deeply equal. Comparison rules, in order:
 *
 * 1. Reference identity (`a === b`) wins immediately.
 * 2. `null` and `undefined` are equal to themselves but not to anything else.
 * 3. Different `typeof` → not equal.
 * 4. `NaN` equals `NaN` (unlike the `===` operator).
 * 5. Other primitives use `===`.
 * 6. Objects with a custom `equals(other)` method delegate to it (record classes).
 * 7. `Array`s compare element-wise in order.
 * 8. `Map`s compare by size, then by key→value pairs (order-independent on the keys).
 * 9. `Set`s compare by size, then by membership (order-independent).
 * 10. `Date`s compare by `getTime()`.
 * 11. Plain objects compare structurally on their own enumerable string keys.
 *
 * Cycles in the input graph are NOT detected — pass cyclic structures at your own risk
 * (the same caveat as `JSON.stringify`). For real-world record/value-object data this
 * has not been a problem.
 */
export function equals(a: unknown, b: unknown): boolean {
  if (a === b) return true;
  if (a == null || b == null) return false;
  if (typeof a !== typeof b) return false;

  // NaN equals NaN — needed because NaN === NaN is false in JS but the C# equivalent
  // (double.NaN.Equals(double.NaN)) returns true.
  if (typeof a === "number") {
    return Number.isNaN(a) && Number.isNaN(b as number);
  }

  // Other primitives are settled by the `===` check above; we only get here for objects.
  if (typeof a !== "object") return false;

  // Custom equals contract from record/value classes.
  if ("equals" in a && typeof (a as { equals?: unknown }).equals === "function") {
    return (a as { equals(other: unknown): boolean }).equals(b);
  }

  if (Array.isArray(a)) {
    if (!Array.isArray(b)) return false;
    if (a.length !== b.length) return false;
    for (let i = 0; i < a.length; i++) {
      if (!equals(a[i], b[i])) return false;
    }
    return true;
  }
  if (Array.isArray(b)) return false;

  if (a instanceof Map) {
    if (!(b instanceof Map)) return false;
    if (a.size !== b.size) return false;
    for (const [key, value] of a) {
      if (!b.has(key)) return false;
      if (!equals(value, b.get(key))) return false;
    }
    return true;
  }
  if (b instanceof Map) return false;

  if (a instanceof Set) {
    if (!(b instanceof Set)) return false;
    if (a.size !== b.size) return false;
    for (const value of a) {
      if (!b.has(value)) return false;
    }
    return true;
  }
  if (b instanceof Set) return false;

  if (a instanceof Date) {
    if (!(b instanceof Date)) return false;
    return a.getTime() === b.getTime();
  }
  if (b instanceof Date) return false;

  // Structural compare on enumerable string keys. Symbol-keyed properties are ignored,
  // matching JSON.stringify and the typical "value object" model.
  const aObj = a as Record<string, unknown>;
  const bObj = b as Record<string, unknown>;
  const aKeys = Object.keys(aObj);
  const bKeys = Object.keys(bObj);
  if (aKeys.length !== bKeys.length) return false;
  for (const key of aKeys) {
    if (!Object.hasOwn(bObj, key)) return false;
    if (!equals(aObj[key], bObj[key])) return false;
  }
  return true;
}

/**
 * Computes a deep hash code for an arbitrary value. The result is consistent with
 * `equals(a, b)` — values that compare equal under `equals` produce the same hash.
 *
 * Rules:
 * 1. `null` / `undefined` → 0.
 * 2. Objects with a custom `hashCode()` method delegate to it.
 * 3. Primitives go through `HashCode.combine`.
 * 4. Arrays hash size + each element in order.
 * 5. `Map` and `Set` hash with an order-independent XOR fold so two Maps/Sets with the
 *    same content but different insertion order get the same hash.
 * 6. `Date`s hash their epoch milliseconds.
 * 7. Plain objects hash their enumerable string keys + values, with the keys sorted so
 *    insertion order doesn't affect the result.
 *
 * Cycle handling matches `equals` — undefined behavior on cyclic graphs.
 */
export function hashCode(value: unknown): number {
  if (value == null) return 0;

  if (typeof value === "object") {
    // Custom hashCode contract from record/value classes.
    if ("hashCode" in value && typeof (value as { hashCode?: unknown }).hashCode === "function") {
      return (value as { hashCode(): number }).hashCode();
    }

    if (Array.isArray(value)) {
      const hc = new HashCode();
      hc.add(value.length);
      for (const item of value) hc.add(hashCode(item));
      return hc.toHashCode();
    }

    if (value instanceof Map) {
      // Order-independent: XOR each (key, value) pair hash. XOR is commutative so the
      // result doesn't depend on insertion order, matching how Map equality works.
      let acc = 0;
      for (const [k, v] of value) {
        acc ^= HashCode.combine2(hashCode(k), hashCode(v));
      }
      return acc;
    }

    if (value instanceof Set) {
      let acc = 0;
      for (const item of value) {
        acc ^= hashCode(item);
      }
      return acc;
    }

    if (value instanceof Date) {
      return hashCode(value.getTime());
    }

    // Plain object: hash sorted keys + values for stable insertion-order independence.
    const obj = value as Record<string, unknown>;
    const keys = Object.keys(obj).sort();
    const hc = new HashCode();
    for (const key of keys) {
      hc.add(key);
      hc.add(hashCode(obj[key]));
    }
    return hc.toHashCode();
  }

  // A 64-bit integer (or BigInteger) hashes as .NET's GetHashCode does —
  // the raw 32-bit fold of its two words (FR-046), not the accumulator mix.
  if (typeof value === "bigint") return hashInt64(value);

  // Primitives.
  return HashCode.combine(value);
}

/**
 * Alias the compiler emits inside synthesized record `equals` methods.
 * Same contract as {@link equals}; the rename keeps the call
 * sites readable next to the surrounding `equals(other)` method
 * declaration (a bare `equals(this.x, other.x)` would shadow into a
 * recursive method-call ambiguity for human readers).
 */
export const valueEquals = equals;

/**
 * C#'s static `object.Equals(a, b)`: true when both are null or `a.Equals(b)`, where
 * `Equals` is what the value's type defines. Unlike {@link equals}, it never compares a
 * class instance by structure, since a C# class that does not override `Equals`
 * compares by reference:
 *
 * 1. Reference identity (`a === b`) wins immediately; `null` / `undefined` equal only
 *    themselves.
 * 2. `NaN` equals `NaN`; other primitives use `===`.
 * 3. Objects with an `equals(other)` method delegate to it (records, user overrides,
 *    `Decimal`, Temporal types).
 * 4. Plain object literals (`[PlainObject]` records, anonymous types) compare by shape
 *    through {@link equals}, as their C# types compare by value.
 * 5. Everything else (class instances, arrays, `Map`s, `Set`s) compares by reference,
 *    as C# arrays, lists, dictionaries and sets do.
 */
export function objectEquals(a: unknown, b: unknown): boolean {
  if (a === b) return true;
  if (a == null || b == null) return false;

  if (typeof a === "number") {
    return typeof b === "number" && Number.isNaN(a) && Number.isNaN(b);
  }

  if (typeof a !== "object") return false;

  if (hasEqualsMethod(a)) return a.equals(b);

  return isPlainObject(a) && isPlainObject(b) && equals(a, b);
}

/**
 * The element structure of a C# tuple, as the compiler emits it for
 * {@link tupleEquals}'s `shape` argument: one entry per positional element, `null`
 * for an element that is not a tuple, a nested `TupleShape` for an element that is
 * itself a tuple (possibly nullable — `null` elements compare equal there). An
 * empty array marks a nested tuple without tuple elements of its own.
 */
export type TupleShape = readonly (TupleShape | null)[];

/**
 * C#'s `ValueTuple.Equals(other)` over the positional arrays C# tuples lower to: same
 * length, and each element pair equal under {@link objectEquals} (C# compares each
 * element with `EqualityComparer<T>.Default`, so a class element compares by reference).
 *
 * At runtime a nested tuple and a C# array are both JS arrays, which `objectEquals`
 * cannot tell apart — so a call whose static tuple type has tuple elements passes the
 * compiler-computed `shape`, and those elements compare element-wise (recursively)
 * instead of by reference. A nullable tuple (or nullable tuple element) is the value
 * or `null` here, so both operands are null-guarded: both `null` equal, one `null`
 * unequal, otherwise element-wise — C#'s `Nullable<T>.Equals` and `object.Equals`
 * answers for nullable tuples.
 */
export function tupleEquals(tuple: unknown, other: unknown, shape?: TupleShape): boolean {
  if (tuple == null || other == null) {
    return tuple == null && other == null;
  }

  if (!Array.isArray(tuple) || !Array.isArray(other) || tuple.length !== other.length) {
    return false;
  }

  return tuple.every((element, index) => {
    const elementShape = shape?.[index];

    return elementShape == null
      ? objectEquals(element, other[index])
      : tupleEquals(element, other[index], elementShape);
  });
}

function hasEqualsMethod(value: object): value is { equals(other: unknown): boolean } {
  return "equals" in value && typeof (value as { equals?: unknown }).equals === "function";
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  if (typeof value !== "object" || value === null) return false;

  const prototype: unknown = Object.getPrototypeOf(value);

  return prototype === Object.prototype || prototype === null;
}

/**
 * Alias the compiler emits for every C# `GetHashCode()` call. Same contract as
 * {@link hashCode}; the distinct name keeps a call site from colliding with a
 * local or a method the user named `hashCode` (records declare one).
 */
export const valueHashCode = hashCode;
