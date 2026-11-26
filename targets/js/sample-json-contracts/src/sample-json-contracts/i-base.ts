/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";

let IBase$rt: ClassType<IBase> | undefined;

export abstract class IBase {
  static get $runtimeType(): ClassType<IBase> {
    return (IBase$rt ??= {
      kind: "class",
      name: "IBase",
      fullName: "SampleJsonContracts.IBase",
      namespace: "SampleJsonContracts",
      ctor: IBase,
      interfaces: [],
      typeParameters: [],
      isAbstract: true,
      isValueType: false,
      test: (v: unknown): v is IBase => v instanceof IBase,
    });
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this);
  }

  hashCode(): number {
    const hc = new HashCode();

    return hc.toHashCode();
  }

  with(overrides?: Partial<IBase>): IBase {
    return copyRecordWith<IBase>(this, overrides);
  }

  protected $printMembers(): string {
    return "";
  }

  toString(): string {
    const members = this.$printMembers();

    return members ? `IBase { ${members} }` : "IBase { }";
  }
}
