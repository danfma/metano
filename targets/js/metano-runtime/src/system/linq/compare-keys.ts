/**
 * Generic key comparator that handles primitives, Temporal types, and
 * objects with a custom `compare()` method (or whose constructor exposes
 * a static `compare(a, b)` — Temporal API shape).
 *
 * Returns negative when `a < b`, positive when `a > b`, zero when equal.
 */
export function compareKeys(a: unknown, b: unknown): number {
  if (a === b) return 0;
  if (a == null) return b == null ? 0 : -1;
  if (b == null) return 1;

  // Object with instance compareTo() / compare() — common .NET-style
  // ergonomics surface when the consumer ports a C# IComparable.
  if (typeof a === "object" && typeof (a as { compare?: unknown }).compare === "function") {
    return (a as { compare: (other: unknown) => number }).compare(b);
  }

  // Temporal types expose static compare() on the constructor.
  if (typeof a === "object") {
    const ctor = (a as { constructor?: unknown }).constructor as
      | { compare?: (x: unknown, y: unknown) => number }
      | undefined;
    if (ctor && typeof ctor.compare === "function") {
      return ctor.compare(a, b);
    }
  }

  // Primitives (string, number, bigint, boolean): default `<` / `>`.
  if ((a as never) < (b as never)) return -1;
  if ((a as never) > (b as never)) return 1;
  return 0;
}
