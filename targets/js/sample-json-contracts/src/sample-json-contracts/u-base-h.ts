/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";

let UBaseH$rt: ClassType<UBaseH> | undefined;

export abstract class UBaseH {
  constructor(readonly a: number) {}

  static get $runtimeType(): ClassType<UBaseH> {
    return (UBaseH$rt ??= {
      kind: "class",
      name: "UBaseH",
      fullName: "SampleJsonContracts.UBaseH",
      namespace: "SampleJsonContracts",
      ctor: UBaseH,
      interfaces: [],
      typeParameters: [],
      isAbstract: true,
      isValueType: false,
      test: (v: unknown): v is UBaseH => v instanceof UBaseH,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.a === other.a
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.a);

    return hc.toHashCode();
  }

  with(overrides?: Partial<UBaseH>): UBaseH {
    return copyRecordWith<UBaseH>(this, overrides);
  }
}
