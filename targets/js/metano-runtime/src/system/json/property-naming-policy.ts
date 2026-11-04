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

// ─── Character classes ──────────────────────────────────────────────────────
// The policies port System.Text.Json's JsonCamelCaseNamingPolicy and
// JsonSeparatorNamingPolicy, so a context built at runtime writes the names the
// transpiler pre-computes and .NET writes, except for characters whose Unicode
// category or casing differs between the JS engine and .NET 10
// (engine-dependent, e.g. U+0295 under Bun). Like .NET they walk UTF-16 code
// units: a surrogate half is no letter.

const uppercaseLetter = /^\p{Lu}$/u;
const lowercaseLetter = /^\p{Ll}$/u;
const decimalDigit = /^\p{Nd}$/u;
const spaceSeparator = /^\p{Zs}$/u;

function isUpper(ch: string | undefined): boolean {
  return ch !== undefined && uppercaseLetter.test(ch);
}

function isLower(ch: string | undefined): boolean {
  return ch !== undefined && lowercaseLetter.test(ch);
}

function isLowerOrDigit(ch: string): boolean {
  return lowercaseLetter.test(ch) || decimalDigit.test(ch);
}

/**
 * .NET's camelCase policy compares with `' '` only: a no-break space
 * (U+00A0) does not count (measured: `AB\u00a0CD` → `aB\u00a0CD`).
 */
function isSpace(ch: string | undefined): boolean {
  return ch === " ";
}

/**
 * .NET's `char.ToLowerInvariant`: a one-to-one mapping, so a character whose
 * lowercase form takes more than one code unit (`İ`) is kept.
 */
function toLowerChar(ch: string): string {
  const lower = ch.toLowerCase();

  return lower.length === 1 ? lower : ch;
}

/** The first code points of the three Greek blocks with ypogegrammeni (`ᾀ`, `ᾐ`, `ᾠ`). */
const iotaSubscriptBlockStarts = [0x1f80, 0x1f90, 0x1fa0] as const;

/** Distance from a lowercase letter with ypogegrammeni to its simple uppercase (`ᾀ` → `ᾈ`). */
const greekCaseOffset = 8;

/** Simple uppercase of the Greek letters with ypogegrammeni, which JS expands to two letters. */
const iotaSubscriptUpper: Readonly<Record<string, string>> = Object.fromEntries([
  ...iotaSubscriptBlockStarts.flatMap((start) =>
    Array.from({ length: greekCaseOffset }, (_, offset) => [
      String.fromCharCode(start + offset),
      String.fromCharCode(start + offset + greekCaseOffset),
    ]),
  ),
  ["\u1fb3", "\u1fbc"], // ᾳ → ᾼ
  ["\u1fc3", "\u1fcc"], // ῃ → ῌ
  ["\u1ff3", "\u1ffc"], // ῳ → ῼ
]);

/**
 * .NET's `char.ToUpperInvariant`: a one-to-one mapping that keeps the dotless
 * `ı` and a character with no single-letter uppercase (`ß`), and maps `ᾀ` to
 * `ᾈ` where JS writes `ἈΙ`.
 */
function toUpperChar(ch: string): string {
  const upper = ch === "ı" ? ch : ch.toUpperCase();

  return upper.length === 1 ? upper : (iotaSubscriptUpper[ch] ?? ch);
}

// ─── Word separation ────────────────────────────────────────────────────────

type SeparatorState =
  | "notStarted"
  | "uppercaseLetter"
  | "lowercaseLetterOrDigit"
  | "spaceSeparator";

/**
 * Whether an uppercase letter followed by `next` starts a new word: after a
 * lowercase letter, a digit or a space, or as the last letter of an uppercase
 * run that a lowercase letter follows (`XMLReader` → `xml_reader`).
 */
function startsWordAt(next: string | undefined, previousState: SeparatorState): boolean {
  switch (previousState) {
    case "lowercaseLetterOrDigit":
    case "spaceSeparator":
      return true;
    case "uppercaseLetter":
      return isLower(next);
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

    if (isUpper(ch)) {
      if (startsWordAt(name[i + 1], state)) {
        result += separator;
      }

      result += lowercase ? toLowerChar(ch) : ch;
      state = "uppercaseLetter";
    } else if (isLowerOrDigit(ch)) {
      if (state === "spaceSeparator") {
        result += separator;
      }

      result += lowercase ? ch : toUpperChar(ch);
      state = "lowercaseLetterOrDigit";
    } else if (spaceSeparator.test(ch)) {
      if (state !== "notStarted") {
        state = "spaceSeparator";
      }
    } else {
      result += ch;
      state = "notStarted";
    }
  }

  return result;
}

// ─── Policy Implementations ─────────────────────────────────────────────────

/**
 * Whether position `i` is the last letter of the leading uppercase run to
 * lowercase: the next character exists and is not an uppercase letter.
 */
function endsLeadingUppercaseRun(chars: readonly string[], i: number): boolean {
  return i > 0 && i + 1 < chars.length && !isUpper(chars[i + 1]);
}

/**
 * A literal port of .NET's `JsonCamelCaseNamingPolicy` (`FixCasing`).
 */
class CamelCasePolicy extends PropertyNamingPolicy {
  /**
   * Lowercases the leading uppercase run, keeping its last letter when a
   * non-uppercase character follows it (`URL` → `url`, `IOStream` →
   * `ioStream`, `ABC_D` → `abC_D`); a name that does not start with an
   * uppercase letter is kept.
   */
  convert(name: string): string {
    if (!isUpper(name[0])) {
      return name;
    }

    const chars = name.split("");

    for (let i = 0; i < chars.length; i++) {
      // A second character that is not uppercase means the run was one
      // letter long: only the first one is lowercased (`Name` → `name`).
      if (i === 1 && !isUpper(chars[i])) {
        break;
      }

      if (endsLeadingUppercaseRun(chars, i)) {
        // .NET also lowercases the run's last letter before a space.
        if (isSpace(chars[i + 1])) {
          chars[i] = toLowerChar(chars[i]!);
        }

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
