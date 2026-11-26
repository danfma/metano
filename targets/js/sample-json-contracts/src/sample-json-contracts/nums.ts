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
import type { Dictionary, List } from "metano-runtime/system/collections";
import type { Decimal } from "metano-runtime/system/decimal";
import { Color$runtimeType, type Color } from "./color.js";

let Nums$rt: ClassType<Nums> | undefined;

export class Nums {
  declare readonly n: number;
  declare readonly m: number | null;
  declare readonly d: Decimal;
  declare readonly x: number;
  declare readonly l: List<number>;
  declare readonly map: Dictionary<string, number>;
  declare readonly c: Color;

  constructor(
    n: number,
    m: number | null,
    d: Decimal,
    x: number,
    l: List<number>,
    map: Dictionary<string, number>,
    c: Color,
  ) {
    this.n = n;
    this.m = m;
    this.d = d;
    this.x = x;
    this.l = l;
    this.map = map;
    this.c = c;
  }

  static get $runtimeType(): ClassType<Nums> {
    return (Nums$rt ??= {
      kind: "class",
      name: "Nums",
      fullName: "SampleJsonContracts.Nums",
      namespace: "SampleJsonContracts",
      ctor: Nums,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is Nums => v instanceof Nums,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.n === other.n &&
      this.m === other.m &&
      valueEquals(this.d, other.d) &&
      valueEquals(this.x, other.x) &&
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

  toString(): string {
    return `Nums { N = ${this.n}, M = ${this.m ?? ""}, D = ${formatText(this.d)}, X = ${formatDouble(this.x)}, L = ${this.l}, Map = ${this.map}, C = ${enumText(this.c, Color$runtimeType)} }`;
  }
}
