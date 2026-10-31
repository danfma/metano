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

  /**
   * Parses a GUID string the way `Guid.TryParse(string, out Guid)` does: the `D`
   * (`8-4-4-4-12`), `N` (32 digits), `B` (`{D}`) and `P` (`(D)`) formats, case-insensitive,
   * with surrounding whitespace ignored. A match is normalized to the lowercase `D` form,
   * the text C#'s `Guid.ToString()` produces; anything else yields `[false, UUID.empty]`.
   * The `X` (hexadecimal braces) format is not supported.
   */
  export function tryParse(
    text: string | null | undefined,
  ): readonly [found: boolean, value: UUID] {
    const digits = guidDigits(text?.trim() ?? "");

    if (digits === null) {
      return [false, empty];
    }

    const canonical = [
      digits.slice(0, 8),
      digits.slice(8, 12),
      digits.slice(12, 16),
      digits.slice(16, 20),
      digits.slice(20),
    ].join("-");

    return [true, canonical.toLowerCase() as UUID];
  }

  const HEX_DIGITS = /^[0-9a-fA-F]{32}$/;

  const HYPHENATED =
    /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

  const ENCLOSURES: Record<string, string> = { "{": "}", "(": ")" };

  /** The 32 hex digits of a `D`, `N`, `B` or `P` GUID string, or `null`. */
  function guidDigits(text: string): string | null {
    if (HEX_DIGITS.test(text)) {
      return text;
    }

    const closing = ENCLOSURES[text.charAt(0)];
    const inner = closing !== undefined && text.endsWith(closing) ? text.slice(1, -1) : text;

    return HYPHENATED.test(inner) ? inner.replace(/-/g, "") : null;
  }
}
