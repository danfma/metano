/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, valueEquals, type ClassType } from "metano-runtime";
import { IShape$runtimeType, type IShape } from "./i-shape";

let Other$rt: ClassType<Other> | undefined;

export class Other implements IShape {
  constructor(readonly side: number) {}

  static get $runtimeType(): ClassType<Other> {
    return (Other$rt ??= {
      kind: "class",
      name: "Other",
      fullName: "SampleJsonContracts.Other",
      namespace: "SampleJsonContracts",
      ctor: Other,
      interfaces: [() => IShape$runtimeType],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is Other => v instanceof Other,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      valueEquals(this.side, other.side)
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.side);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Other>): Other {
    return copyRecordWith<Other>(this, overrides);
  }
}
