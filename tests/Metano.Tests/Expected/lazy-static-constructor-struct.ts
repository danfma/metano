import { HashCode, typeInitializer } from "metano-runtime";
import { Seed } from "./seed.js";

export class Cell {
  static readonly #$init = /* @__PURE__ */ typeInitializer("Cell", () => {
  }) as () => void;

  v: number = 0;
  w: number = 0;

  constructor(v: number) {
    Cell.#$init();
    this.w = Seed.next();
    this.v = v;
  }

  get twice(): number {
    Cell.#$init();

    return Math.imul(this.v, 2);
  }

  static $initialize(): void {
    Cell.#$init();
  }

  get(): number {
    Cell.#$init();

    return this.v;
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.v === other.v && this.w === other.w;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.v);
    hc.add(this.w);

    return hc.toHashCode();
  }
}
