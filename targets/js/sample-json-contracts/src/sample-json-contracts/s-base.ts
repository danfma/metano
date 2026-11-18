/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";

let SBase$rt: ClassType<SBase> | undefined;

export class SBase {
  constructor(readonly v: number) {}

  static get $runtimeType(): ClassType<SBase> {
    return (SBase$rt ??= {
      kind: "class",
      name: "SBase",
      fullName: "SampleJsonContracts.SBase",
      namespace: "SampleJsonContracts",
      ctor: SBase,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is SBase => v instanceof SBase,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.v === other.v
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.v);

    return hc.toHashCode();
  }

  with(overrides?: Partial<SBase>): SBase {
    return copyRecordWith<SBase>(this, overrides);
  }
}
