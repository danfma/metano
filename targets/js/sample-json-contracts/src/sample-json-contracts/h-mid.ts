/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import { HBase } from "./h-base.js";

let HMid$rt: ClassType<HMid> | undefined;

export class HMid extends HBase {
  constructor(
    a: number,
    readonly b: number,
  ) {
    super(a);
  }

  static override get $runtimeType(): ClassType<HMid> {
    return (HMid$rt ??= {
      kind: "class",
      name: "HMid",
      fullName: "SampleJsonContracts.HMid",
      namespace: "SampleJsonContracts",
      ctor: HMid,
      base: () => HBase.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is HMid => v instanceof HMid,
    });
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.b === other.b;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.b);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<HMid>): HMid {
    return copyRecordWith<HMid>(this, overrides);
  }

  protected override $printMembers(): string {
    const base = super.$printMembers?.() ?? "";

    return `${base ? `${base}, ` : ""}B = ${this.b}`;
  }

  override toString(): string {
    return `HMid { ${this.$printMembers()} }`;
  }
}
