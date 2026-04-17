/**
 * Deep value-equality and standalone hashing helpers for arbitrary JavaScript values.
 * Mirror the C# .NET semantics: structural equality for compounds, NaN-equals-NaN for
 * floats, custom `equals(other)` / `hashCode()` methods on user types take precedence.
 *
 * These are utility helpers for user code — the compiler does NOT call them. Generated
 * record classes have their own inline `equals(other)` / `hashCode()` methods that
 * compare each captured constructor parameter directly. Use these when you have an
 * arbitrary value of unknown shape and need a structural compare or a content-based
 * hash key (e.g., to feed into a `HashSet<T>` over plain object literals).
 */
import { HashCode } from "./hash-code.ts";

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

  // Primitives.
  return HashCode.combine(value);
}
