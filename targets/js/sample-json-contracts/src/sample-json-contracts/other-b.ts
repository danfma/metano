/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import {
  HashCode,
  copyRecordWith,
  formatDouble,
  valueEquals,
  type ClassType,
} from "metano-runtime";
import { IShapeB$runtimeType, type IShapeB } from "./i-shape-b.js";

let OtherB$rt: ClassType<OtherB> | undefined;

export class OtherB implements IShapeB {
  declare readonly area: number;

  constructor(area: number) {
    this.area = area;
  }

  static get $runtimeType(): ClassType<OtherB> {
    return (OtherB$rt ??= {
      kind: "class",
      name: "OtherB",
      fullName: "SampleJsonContracts.OtherB",
      namespace: "SampleJsonContracts",
      ctor: OtherB,
      interfaces: [() => IShapeB$runtimeType],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is OtherB => v instanceof OtherB,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      valueEquals(this.area, other.area)
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.area);

    return hc.toHashCode();
  }

  with(overrides?: Partial<OtherB>): OtherB {
    return copyRecordWith<OtherB>(this, overrides);
  }

  toString(): string {
    return `OtherB { Area = ${formatDouble(this.area)} }`;
  }
}
