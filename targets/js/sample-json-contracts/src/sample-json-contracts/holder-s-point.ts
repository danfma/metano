/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, valueEquals, type ClassType } from "metano-runtime";
import type { SPoint } from "./s-point.js";

let HolderSPoint$rt: ClassType<HolderSPoint> | undefined;

export class HolderSPoint {
  declare readonly s: SPoint;

  constructor(s: SPoint) {
    this.s = s;
  }

  static get $runtimeType(): ClassType<HolderSPoint> {
    return (HolderSPoint$rt ??= {
      kind: "class",
      name: "HolderSPoint",
      fullName: "SampleJsonContracts.HolderSPoint",
      namespace: "SampleJsonContracts",
      ctor: HolderSPoint,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is HolderSPoint => v instanceof HolderSPoint,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      valueEquals(this.s, other.s)
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.s);

    return hc.toHashCode();
  }

  with(overrides?: Partial<HolderSPoint>): HolderSPoint {
    return copyRecordWith<HolderSPoint>(this, overrides);
  }

  protected $printMembers(): string {
    return `S = ${this.s}`;
  }

  toString(): string {
    return `HolderSPoint { ${this.$printMembers()} }`;
  }
}
