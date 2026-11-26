/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import { NBase } from "./n-base.js";

let NMid$rt: ClassType<NMid> | undefined;

export class NMid extends NBase {
  declare readonly b: number;

  constructor(a: number, b: number) {
    super(a);
    this.b = b;
  }

  static override get $runtimeType(): ClassType<NMid> {
    return (NMid$rt ??= {
      kind: "class",
      name: "NMid",
      fullName: "SampleJsonContracts.NMid",
      namespace: "SampleJsonContracts",
      ctor: NMid,
      base: () => NBase.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is NMid => v instanceof NMid,
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

  override with(overrides?: Partial<NMid>): NMid {
    return copyRecordWith<NMid>(this, overrides);
  }

  protected override $printMembers(): string {
    const base = super.$printMembers?.() ?? "";

    return `${base ? `${base}, ` : ""}B = ${this.b}`;
  }

  override toString(): string {
    return `NMid { ${this.$printMembers()} }`;
  }
}
