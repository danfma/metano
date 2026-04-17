/**
 * Generic key comparator that handles primitives, Temporal types, and
 * objects with a custom compareTo() method.
 *
 * Returns negative if a < b, positive if a > b, 0 if equal.
 */
export function compareKeys(a: unknown, b: unknown): number {
  if (a === b) return 0;
  if (a == null) return b == null ? 0 : -1;
  if (b == null) return 1;

  // Objects with compareTo() (e.g., Temporal types)
  if (typeof a === "object" && a !== null && typeof (a as any).compare === "function") {
    return (a as any).compare(b);
  }

  // Temporal types expose static compare() — try to detect via constructor
  if (typeof a === "object" && a !== null) {
    const ctor = (a as any).constructor;
    if (ctor && typeof ctor.compare === "function") {
      return ctor.compare(a, b);
    }
  }

  // Primitives (string, number, bigint, boolean): use < / >
  if (a < (b as any)) return -1;
  if (a > (b as any)) return 1;
  return 0;
}
