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

/**
 * Splits a PascalCase or camelCase name into words. Handles:
 * - Uppercase transitions: "FirstName" → ["First", "Name"]
 * - Acronyms: "HTMLParser" → ["HTML", "Parser"]
 * - Numbers: "Item2Count" → ["Item", "2", "Count"]
 */
function splitWords(name: string): string[] {
  const words: string[] = [];
  let current = "";

  for (let i = 0; i < name.length; i++) {
    const ch = name[i]!;
    const next = name[i + 1];

    if (i > 0 && isUpperCase(ch)) {
      if (!isUpperCase(name[i - 1]!)) {
        words.push(current);
        current = ch;
      } else if (next && !isUpperCase(next) && !isDigit(next)) {
        words.push(current);
        current = ch;
      } else {
        current += ch;
      }
    } else if (i > 0 && isDigit(ch) && !isDigit(name[i - 1]!)) {
      words.push(current);
      current = ch;
    } else if (i > 0 && !isDigit(ch) && isDigit(name[i - 1]!)) {
      words.push(current);
      current = ch;
    } else {
      current += ch;
    }
  }

  if (current) {
    words.push(current);
  }

  return words;
}

function isUpperCase(ch: string): boolean {
  return ch >= "A" && ch <= "Z";
}

function isDigit(ch: string): boolean {
  return ch >= "0" && ch <= "9";
}

// ─── Policy Implementations ─────────────────────────────────────────────────

class CamelCasePolicy extends PropertyNamingPolicy {
  convert(name: string): string {
    if (!name) return name;
    let i = 0;
    while (i < name.length && isUpperCase(name[i]!)) {
      i++;
    }
    if (i === 0) return name;
    if (i === 1) return name[0]!.toLowerCase() + name.slice(1);
    if (i === name.length) return name.toLowerCase(); // all uppercase: "ID" → "id"
    return name.slice(0, i - 1).toLowerCase() + name.slice(i - 1);
  }
}

class SnakeCaseLowerPolicy extends PropertyNamingPolicy {
  convert(name: string): string {
    return splitWords(name)
      .map((w) => w.toLowerCase())
      .join("_");
  }
}

class SnakeCaseUpperPolicy extends PropertyNamingPolicy {
  convert(name: string): string {
    return splitWords(name)
      .map((w) => w.toUpperCase())
      .join("_");
  }
}

class KebabCaseLowerPolicy extends PropertyNamingPolicy {
  convert(name: string): string {
    return splitWords(name)
      .map((w) => w.toLowerCase())
      .join("-");
  }
}

class KebabCaseUpperPolicy extends PropertyNamingPolicy {
  convert(name: string): string {
    return splitWords(name)
      .map((w) => w.toUpperCase())
      .join("-");
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
