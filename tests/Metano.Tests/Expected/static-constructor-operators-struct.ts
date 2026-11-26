import { HashCode, typeInitializer } from "metano-runtime";

export class Pt {
  static readonly #$init = /* @__PURE__ */ typeInitializer("Pt", () => {
    Pt.origin = 1;
  }) as () => void;

  static #origin$: number = 0;
  declare x: number;

  constructor(x: number) {
    Pt.#$init();
    this.x = x;
  }

  static get origin(): number {
    Pt.#$init();

    return Pt.#origin$;
  }

  private static set origin(value: number) {
    Pt.#$init();
    Pt.#origin$ = value;
  }

  static $initialize(): void {
    Pt.#$init();
  }

  static __subtract(a: Pt, b: Pt): Pt {
    Pt.#$init();

    return new Pt((a.x - b.x) | 0);
  }

  $subtract(b: Pt): Pt {
    Pt.#$init();

    return Pt.__subtract(this, b);
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.x === other.x;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.x);

    return hc.toHashCode();
  }
}
