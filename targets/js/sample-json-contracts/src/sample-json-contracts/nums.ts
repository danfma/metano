/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, valueEquals } from "metano-runtime";
import type { Decimal } from "decimal.js";
import type { Color } from "./color";

export class Nums {
  constructor(
    readonly n: number,
    readonly m: number | null,
    readonly d: Decimal,
    readonly x: number,
    readonly l: number[],
    readonly map: Map<string, number>,
    readonly c: Color,
  ) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.n === other.n &&
      this.m === other.m &&
      valueEquals(this.d, other.d) &&
      this.x === other.x &&
      this.l === other.l &&
      this.map === other.map &&
      this.c === other.c
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.n);
    hc.add(this.m);
    hc.add(this.d);
    hc.add(this.x);
    hc.add(this.l);
    hc.add(this.map);
    hc.add(this.c);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Nums>): Nums {
    return copyRecordWith<Nums>(this, overrides);
  }
}
