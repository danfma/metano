/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import { UFail } from "./u-fail.js";

let UFailY$rt: ClassType<UFailY> | undefined;

export class UFailY extends UFail {
  constructor() {
    super();
  }

  static override get $runtimeType(): ClassType<UFailY> {
    return (UFailY$rt ??= {
      kind: "class",
      name: "UFailY",
      fullName: "SampleJsonContracts.UFailY",
      namespace: "SampleJsonContracts",
      ctor: UFailY,
      base: () => UFail.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new UFailY(),
      test: (v: unknown): v is UFailY => v instanceof UFailY,
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

  override with(overrides?: Partial<UFailY>): UFailY {
    return copyRecordWith<UFailY>(this, overrides);
  }

  protected override $printMembers(): string {
    return super.$printMembers?.() ?? "";
  }

  override toString(): string {
    const members = this.$printMembers();

    return members ? `UFailY { ${members} }` : "UFailY { }";
  }
}
