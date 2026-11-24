/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import { UBaseH } from "./u-base-h";

let UBaseA$rt: ClassType<UBaseA> | undefined;

export class UBaseA extends UBaseH {
  constructor(a: number) {
    super(a);
  }

  static override get $runtimeType(): ClassType<UBaseA> {
    return (UBaseA$rt ??= {
      kind: "class",
      name: "UBaseA",
      fullName: "SampleJsonContracts.UBaseA",
      namespace: "SampleJsonContracts",
      ctor: UBaseA,
      base: () => UBaseH.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is UBaseA => v instanceof UBaseA,
    });
  }

  override equals(other: any): boolean {
    return super.equals(other);
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());

    return hc.toHashCode();
  }

  override with(overrides?: Partial<UBaseA>): UBaseA {
    return copyRecordWith<UBaseA>(this, overrides);
  }

  protected override $printMembers(): string {
    return super.$printMembers?.() ?? "";
  }

  override toString(): string {
    const members = this.$printMembers();

    return members ? `UBaseA { ${members} }` : "UBaseA { }";
  }
}
