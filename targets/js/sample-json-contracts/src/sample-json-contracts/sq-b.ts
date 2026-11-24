/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import {
  HashCode,
  copyRecordWith,
  formatDouble,
  valueEquals,
  type ClassType,
} from "metano-runtime";
import { IShapeB$runtimeType, type IShapeB } from "./i-shape-b";

let SqB$rt: ClassType<SqB> | undefined;

export class SqB implements IShapeB {
  constructor(
    readonly area: number,
    readonly side: number,
  ) {}

  static get $runtimeType(): ClassType<SqB> {
    return (SqB$rt ??= {
      kind: "class",
      name: "SqB",
      fullName: "SampleJsonContracts.SqB",
      namespace: "SampleJsonContracts",
      ctor: SqB,
      interfaces: [() => IShapeB$runtimeType],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is SqB => v instanceof SqB,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      valueEquals(this.area, other.area) &&
      valueEquals(this.side, other.side)
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.area);
    hc.add(this.side);

    return hc.toHashCode();
  }

  with(overrides?: Partial<SqB>): SqB {
    return copyRecordWith<SqB>(this, overrides);
  }

  toString(): string {
    return `SqB { Area = ${formatDouble(this.area)}, Side = ${formatDouble(this.side)} }`;
  }
}
