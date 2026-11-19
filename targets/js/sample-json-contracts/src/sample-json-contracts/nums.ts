/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, valueEquals, type ClassType } from "metano-runtime";
import type { Dictionary, List } from "metano-runtime/system/collections";
import type { Decimal } from "metano-runtime/system/decimal";
import type { Color } from "./color";

let Nums$rt: ClassType<Nums> | undefined;

export class Nums {
  constructor(
    readonly n: number,
    readonly m: number | null,
    readonly d: Decimal,
    readonly x: number,
    readonly l: List<number>,
    readonly map: Dictionary<string, number>,
    readonly c: Color,
  ) {}

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
}
