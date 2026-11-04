/**
 * Naming policy for converting C# PascalCase property names to JSON wire names.
 *
 * The transpiler pre-computes all JSON names at compile time, so this class is
 * only needed when constructing a SerializerContext manually at runtime.
 */
export abstract class PropertyNamingPolicy {
  abstract convert(name: string): string;

  static readonly camelCase: PropertyNamingPolicy;
  static readonly snakeCaseLower: PropertyNamingPolicy;
  static readonly snakeCaseUpper: PropertyNamingPolicy;
  static readonly kebabCaseLower: PropertyNamingPolicy;
  static readonly kebabCaseUpper: PropertyNamingPolicy;
}

// ─── Helpers ─────────────────────────────────────────────────────────────────
// The policies port System.Text.Json's JsonCamelCaseNamingPolicy and
// JsonSeparatorNamingPolicy, so a context built at runtime writes the names the
// transpiler pre-computes and .NET writes. Like .NET they walk UTF-16 code
// units: a surrogate half is no letter.

const uppercaseLetter = /^\p{Lu}$/u;
const lowercaseLetter = /^\p{Ll}$/u;
const decimalDigit = /^\p{Nd}$/u;
const spaceSeparator = /^\p{Zs}$/u;

function isUpper(ch: string | undefined): boolean {
  return ch !== undefined && uppercaseLetter.test(ch);
}

/**
 * .NET's `char.ToLowerInvariant`: a one-to-one mapping, so a character whose
 * lowercase form takes more than one code unit (`İ`) is kept.
 */
function toLowerChar(ch: string): string {
  const lower = ch.toLowerCase();

  return lower.length === 1 ? lower : ch;
}

/** Simple uppercase of the Greek letters with ypogegrammeni, which JS expands to two letters. */
const greekIotaSubscriptUpper: Readonly<Record<string, string>> = {
  ...Object.fromEntries(
    [0x1f80, 0x1f90, 0x1fa0].flatMap((start) =>
      Array.from({ length: 8 }, (_, offset) => [
        String.fromCharCode(start + offset),
        String.fromCharCode(start + offset + 8),
      ]),
    ),
  ),
  "\u1fb3": "\u1fbc",
  "\u1fc3": "\u1fcc",
  "\u1ff3": "\u1ffc",
};

/**
 * .NET's `char.ToUpperInvariant`: a one-to-one mapping that keeps the dotless
 * `ı` and a character with no single-letter uppercase (`ß`), and maps `ᾀ` to
 * `ᾈ` where JS writes `ἈΙ`.
 */
function toUpperChar(ch: string): string {
  const upper = ch === "ı" ? ch : ch.toUpperCase();

  return upper.length === 1 ? upper : (greekIotaSubscriptUpper[ch] ?? ch);
}

type SeparatorState =
  | "notStarted"
  | "uppercaseLetter"
  | "lowercaseLetterOrDigit"
  | "spaceSeparator";

/**
 * Whether the uppercase letter at `index` starts a new word: after a lowercase
 * letter, a digit or a space, or as the last letter of an uppercase run that a
 * lowercase letter follows (`XMLReader` → `xml_reader`).
 */
function startsWordAt(name: string, index: number, previousState: SeparatorState): boolean {
  switch (previousState) {
    case "lowercaseLetterOrDigit":
    case "spaceSeparator":
      return true;
    case "uppercaseLetter":
      return lowercaseLetter.test(name[index + 1] ?? "");
    default:
      return false;
  }
}

/**
 * Joins the words of `name` with `separator` (`Item_Name` → `item_name`,
 * `Value2` → `value2`, `A1B` → `a1_b`). Spaces collapse into one separator
 * between words and are dropped at either end; any other character (`_`, a
 * surrogate half) is written as is and the next letter does not start a new
 * word.
 */
function separate(name: string, separator: string, lowercase: boolean): string {
  let result = "";
  let state: SeparatorState = "notStarted";

  for (let i = 0; i < name.length; i++) {
    const ch = name[i]!;

    if (uppercaseLetter.test(ch)) {
      if (startsWordAt(name, i, state)) result += separator;

      result += lowercase ? toLowerChar(ch) : ch;
      state = "uppercaseLetter";
    } else if (lowercaseLetter.test(ch) || decimalDigit.test(ch)) {
      if (state === "spaceSeparator") result += separator;

      result += lowercase ? ch : toUpperChar(ch);
      state = "lowercaseLetterOrDigit";
    } else if (spaceSeparator.test(ch)) {
      if (state !== "notStarted") state = "spaceSeparator";
    } else {
      result += ch;
      state = "notStarted";
    }
  }

  return result;
}

// ─── Policy Implementations ─────────────────────────────────────────────────

class CamelCasePolicy extends PropertyNamingPolicy {
  /**
   * Lowercases the leading uppercase run, keeping its last letter when a
   * non-uppercase character follows it (`URL` → `url`, `IOStream` →
   * `ioStream`, `ABC_D` → `abC_D`); a name that does not start with an
   * uppercase letter is kept.
   */
  convert(name: string): string {
    if (!isUpper(name[0])) return name;

    const chars = name.split("");

    for (let i = 0; i < chars.length; i++) {
      if (i === 1 && !isUpper(chars[i])) break;

      const hasNext = i + 1 < chars.length;

      if (i > 0 && hasNext && !isUpper(chars[i + 1])) {
        if (chars[i + 1] === " ") chars[i] = toLowerChar(chars[i]!);

        break;
      }

      chars[i] = toLowerChar(chars[i]!);
    }

    return chars.join("");
  }
}

class SnakeCaseLowerPolicy extends PropertyNamingPolicy {
  convert(name: string): string {
    return separate(name, "_", true);
  }
}

class SnakeCaseUpperPolicy extends PropertyNamingPolicy {
  convert(name: string): string {
    return separate(name, "_", false);
  }
}

class KebabCaseLowerPolicy extends PropertyNamingPolicy {
  convert(name: string): string {
    return separate(name, "-", true);
  }
}

class KebabCaseUpperPolicy extends PropertyNamingPolicy {
  convert(name: string): string {
    return separate(name, "-", false);
  }
}

// ─── Static Initialization ──────────────────────────────────────────────────
// Assigned after subclasses are defined to avoid TDZ issues.

const pnp = PropertyNamingPolicy as unknown as Record<string, PropertyNamingPolicy>;
pnp.camelCase = new CamelCasePolicy();
pnp.snakeCaseLower = new SnakeCaseLowerPolicy();
pnp.snakeCaseUpper = new SnakeCaseUpperPolicy();
pnp.kebabCaseLower = new KebabCaseLowerPolicy();
pnp.kebabCaseUpper = new KebabCaseUpperPolicy();
