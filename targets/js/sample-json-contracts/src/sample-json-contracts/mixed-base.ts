/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";

let MixedBase$rt: ClassType<MixedBase> | undefined;

export abstract class MixedBase {
  static get $runtimeType(): ClassType<MixedBase> {
    return (MixedBase$rt ??= {
      kind: "class",
      name: "MixedBase",
      fullName: "SampleJsonContracts.MixedBase",
      namespace: "SampleJsonContracts",
      ctor: MixedBase,
      interfaces: [],
      typeParameters: [],
      isAbstract: true,
      isValueType: false,
      test: (v: unknown): v is MixedBase => v instanceof MixedBase,
    });
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this);
  }

  hashCode(): number {
    const hc = new HashCode();

    return hc.toHashCode();
  }

  with(overrides?: Partial<MixedBase>): MixedBase {
    return copyRecordWith<MixedBase>(this, overrides);
  }

  protected $printMembers(): string {
    return "";
  }

  toString(): string {
    const members = this.$printMembers();

    return members ? `MixedBase { ${members} }` : "MixedBase { }";
  }
}
