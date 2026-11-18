/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, valueEquals, type ClassType } from "metano-runtime";
import { IShape$runtimeType, type IShape } from "./i-shape";

let Sq$rt: ClassType<Sq> | undefined;

export class Sq implements IShape {
  constructor(readonly side: number) {}

  static get $runtimeType(): ClassType<Sq> {
    return (Sq$rt ??= {
      kind: "class",
      name: "Sq",
      fullName: "SampleJsonContracts.Sq",
      namespace: "SampleJsonContracts",
      ctor: Sq,
      interfaces: [() => IShape$runtimeType],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is Sq => v instanceof Sq,
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

  with(overrides?: Partial<Sq>): Sq {
    return copyRecordWith<Sq>(this, overrides);
  }
}
