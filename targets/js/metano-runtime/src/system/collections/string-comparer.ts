/**
 * `System.StringComparer.Ordinal` and `OrdinalIgnoreCase` as the runtime's comparers
 * (#687): the argument a `Dictionary<string, V>`, a `HashSet<string>` or a sort takes. The
 * culture-sensitive comparers have no counterpart and stay unmapped.
 */
import { ordinalIgnoreCaseKeyOf } from "../json/ordinal-ignore-case.ts";
import { elementHashCode, type IComparer, type IEqualityComparer } from "./default-comparer.ts";

/* The singletons are built on first read, so importing the module runs no code (tree shaking). */
let ordinal: StringComparer | undefined;
let ordinalIgnoreCase: StringComparer | undefined;

export class StringComparer implements IEqualityComparer<string | null>, IComparer<string | null> {
  private readonly ignoreCase: boolean;

  private constructor(ignoreCase: boolean) {
    this.ignoreCase = ignoreCase;
  }

  /** Every code unit compared as is. */
  static get ordinal(): StringComparer {
    return (ordinal ??= new StringComparer(false));
  }

  /** Strings that differ only by the simple uppercase mapping of their code points are equal. */
  static get ordinalIgnoreCase(): StringComparer {
    return (ordinalIgnoreCase ??= new StringComparer(true));
  }

  equals(a: string | null, b: string | null): boolean {
    return this.keyOf(a) === this.keyOf(b);
  }

  getHashCode(value: string | null): number {
    return elementHashCode(this.keyOf(value));
  }

  /** `null` sorts first; the rest by UTF-16 code unit order (of the uppercased text when ignoring case). */
  compare(a: string | null, b: string | null): number {
    const left = this.keyOf(a);
    const right = this.keyOf(b);

    if (left === right) {
      return 0;
    }

    if (left === null) {
      return -1;
    }

    return right === null || left > right ? 1 : -1;
  }

  private keyOf(value: string | null): string | null {
    return this.ignoreCase && value !== null ? ordinalIgnoreCaseKeyOf(value) : value;
  }
}
