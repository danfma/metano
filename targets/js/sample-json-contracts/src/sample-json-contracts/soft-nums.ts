/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, valueEquals, type ClassType } from "metano-runtime";
import type { Decimal } from "metano-runtime/system/decimal";
import type { Color } from "./color.js";

let SoftNums$rt: ClassType<SoftNums> | undefined;

export class SoftNums {
  constructor(
    readonly n: number | null,
    readonly d: Decimal | null,
    readonly x: number | null,
    readonly c: Color | null,
  ) {}

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
}
