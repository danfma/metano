/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, valueEquals } from "metano-runtime";
import type { Decimal } from "decimal.js";
import type { Color } from "./color";

export class SoftNums {
  constructor(
    readonly n: number | null,
    readonly d: Decimal | null,
    readonly x: number | null,
    readonly c: Color | null,
  ) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.n === other.n &&
      valueEquals(this.d, other.d) &&
      this.x === other.x &&
      this.c === other.c
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.n);
    hc.add(this.d);
    hc.add(this.x);
    hc.add(this.c);

    return hc.toHashCode();
  }

  with(overrides?: Partial<SoftNums>): SoftNums {
    return copyRecordWith<SoftNums>(this, overrides);
  }
}
