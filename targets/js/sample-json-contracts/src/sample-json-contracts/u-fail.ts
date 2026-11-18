/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";

let UFail$rt: ClassType<UFail> | undefined;

export class UFail {
  constructor() {}

  static get $runtimeType(): ClassType<UFail> {
    return (UFail$rt ??= {
      kind: "class",
      name: "UFail",
      fullName: "SampleJsonContracts.UFail",
      namespace: "SampleJsonContracts",
      ctor: UFail,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new UFail(),
      test: (v: unknown): v is UFail => v instanceof UFail,
    });
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this);
  }

  hashCode(): number {
    const hc = new HashCode();

    return hc.toHashCode();
  }

  with(overrides?: Partial<UFail>): UFail {
    return copyRecordWith<UFail>(this, overrides);
  }
}
