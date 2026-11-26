/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import {
  HashCode,
  copyRecordWith,
  enumText,
  formatDouble,
  formatText,
  valueEquals,
  type ClassType,
} from "metano-runtime";
import type { Decimal } from "metano-runtime/system/decimal";
import { Color$runtimeType, type Color } from "./color.js";

let SoftNums$rt: ClassType<SoftNums> | undefined;

export class SoftNums {
  declare readonly n: number | null;
  declare readonly d: Decimal | null;
  declare readonly x: number | null;
  declare readonly c: Color | null;

  constructor(n: number | null, d: Decimal | null, x: number | null, c: Color | null) {
    this.n = n;
    this.d = d;
    this.x = x;
    this.c = c;
  }

  static get $runtimeType(): ClassType<SoftNums> {
    return (SoftNums$rt ??= {
      kind: "class",
      name: "SoftNums",
      fullName: "SampleJsonContracts.SoftNums",
      namespace: "SampleJsonContracts",
      ctor: SoftNums,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is SoftNums => v instanceof SoftNums,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.n === other.n &&
      valueEquals(this.d, other.d) &&
      valueEquals(this.x, other.x) &&
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

  toString(): string {
    return `SoftNums { N = ${this.n ?? ""}, D = ${formatText(this.d)}, X = ${formatDouble(this.x)}, C = ${enumText(this.c, Color$runtimeType)} }`;
  }
}
