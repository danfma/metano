/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import { UNear } from "./u-near";

let UNearAbs$rt: ClassType<UNearAbs> | undefined;

export abstract class UNearAbs extends UNear {
  constructor(a: number) {
    super(a);
  }

  static override get $runtimeType(): ClassType<UNearAbs> {
    return (UNearAbs$rt ??= {
      kind: "class",
      name: "UNearAbs",
      fullName: "SampleJsonContracts.UNearAbs",
      namespace: "SampleJsonContracts",
      ctor: UNearAbs,
      base: () => UNear.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: true,
      isValueType: false,
      test: (v: unknown): v is UNearAbs => v instanceof UNearAbs,
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

  override with(overrides?: Partial<UNearAbs>): UNearAbs {
    return copyRecordWith<UNearAbs>(this, overrides);
  }

  protected override $printMembers(): string {
    return super.$printMembers?.() ?? "";
  }

  override toString(): string {
    const members = this.$printMembers();

    return members ? `UNearAbs { ${members} }` : "UNearAbs { }";
  }
}
