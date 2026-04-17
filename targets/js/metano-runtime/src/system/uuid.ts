/**
 * UUID — a branded primitive type for values that represent a RFC 4122 UUID.
 *
 * At runtime a `UUID` is literally a `string` (the brand is a compile-time
 * only marker), so serialization, logging, and interop with ordinary string
 * APIs "just work" with zero overhead. The brand exists purely so the type
 * system distinguishes "an arbitrary string" from "a validated UUID", which
 * matches the intent of C#'s `System.Guid`.
 *
 * This is the type Metano emits for `System.Guid` in transpiled C# code.
 */
export type UUID = string & { readonly __brand: "UUID" };

// The namespace pattern mirrors generated [InlineWrapper] output so Metano
// can treat UUID exactly like a user-defined branded type — including in the
// JSON serializer's `branded` descriptor.
export namespace UUID {
  /**
   * Wraps an existing string as a `UUID` without validation. The caller is
   * responsible for ensuring the string actually looks like a UUID — this is
   * the escape hatch for values that come from trusted sources (the server,
   * the database, `newUuid()` itself).
   */
  export function create(value: string): UUID {
    return value as UUID;
  }

  /**
   * Generates a fresh random UUID in the canonical RFC 4122 form
   * (`"xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"`). Mirrors `Guid.NewGuid()`.
   */
  export function newUuid(): UUID {
    return crypto.randomUUID() as UUID;
  }

  /**
   * Generates a fresh random UUID in the compact form (no hyphens). Mirrors
   * `Guid.NewGuid().ToString("N")`.
   */
  export function newCompact(): UUID {
    return crypto.randomUUID().replace(/-/g, "") as UUID;
  }

  /**
   * The canonical all-zero UUID (`"00000000-0000-0000-0000-000000000000"`).
   * Mirrors `Guid.Empty`.
   */
  export const empty: UUID = "00000000-0000-0000-0000-000000000000" as UUID;

  /**
   * Type guard — returns true if `value` is a string shaped like a UUID
   * (either canonical with hyphens or compact without). The check is a
   * regular expression match, not full RFC 4122 version/variant validation.
   */
  export function isUuid(value: unknown): value is UUID {
    if (typeof value !== "string") return false;
    // 8-4-4-4-12 hex groups (canonical form) OR 32 contiguous hex chars (N form)
    return /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$|^[0-9a-fA-F]{32}$/.test(
      value,
    );
  }
}
