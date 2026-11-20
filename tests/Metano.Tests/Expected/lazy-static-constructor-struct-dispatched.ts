import { HashCode, isInt32, isString, typeInitializer } from "metano-runtime";

export class Pair {
  static readonly #$init = /* @__PURE__ */ typeInitializer("Pair", () => {
  }) as () => void;

  protected static readonly byPairV = /* @__PURE__ */ Symbol("Pair(int v)");
  protected static readonly byPairText = /* @__PURE__ */ Symbol("Pair(string text)");
  w: number = 0;
  v: number = 0;

  constructor(overload: typeof Pair.byPairV, v: number);
  constructor(overload: typeof Pair.byPairText, text: string);
  constructor();
  constructor(v: number);
  constructor(text: string);
  constructor(...args: unknown[]) {
    if (args.length === 0) {
      this.w = 0;
      this.v = 0;

      return;
    }

    Pair.#$init();
    this.w = 7;

    if (typeof args[0] === "symbol" && args[0] === Pair.byPairV) {
      const v = args[1] as number;
      this.v = v;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Pair.byPairText) {
      const text = args[1] as string;
      this.v = text.length;

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const v = args[0] as number;
      this.v = v;

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      const text = args[0] as string;
      this.v = text.length;

      return;
    }

    throw new Error("No matching constructor");
  }

  static $initialize(): void {
    Pair.#$init();
  }

  static fromV(v: number): Pair {
    return new Pair(Pair.byPairV, v);
  }

  static fromText(text: string): Pair {
    return new Pair(Pair.byPairText, text);
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.w === other.w && this.v === other.v;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.w);
    hc.add(this.v);

    return hc.toHashCode();
  }
}
