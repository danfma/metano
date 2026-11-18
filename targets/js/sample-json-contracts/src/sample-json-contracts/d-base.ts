/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";

let DBase$rt: ClassType<DBase> | undefined;

export class DBase {
  constructor() {}

  static get $runtimeType(): ClassType<DBase> {
    return (DBase$rt ??= {
      kind: "class",
      name: "DBase",
      fullName: "SampleJsonContracts.DBase",
      namespace: "SampleJsonContracts",
      ctor: DBase,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new DBase(),
      test: (v: unknown): v is DBase => v instanceof DBase,
    });
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this);
  }

  hashCode(): number {
    const hc = new HashCode();

    return hc.toHashCode();
  }

  with(overrides?: Partial<DBase>): DBase {
    return copyRecordWith<DBase>(this, overrides);
  }
}
