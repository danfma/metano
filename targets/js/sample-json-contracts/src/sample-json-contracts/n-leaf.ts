/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import { NMid } from "./n-mid.js";

let NLeaf$rt: ClassType<NLeaf> | undefined;

export class NLeaf extends NMid {
  constructor(
    a: number,
    b: number,
    readonly c: number,
  ) {
    super(a, b);
  }

  static override get $runtimeType(): ClassType<NLeaf> {
    return (NLeaf$rt ??= {
      kind: "class",
      name: "NLeaf",
      fullName: "SampleJsonContracts.NLeaf",
      namespace: "SampleJsonContracts",
      ctor: NLeaf,
      base: () => NMid.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is NLeaf => v instanceof NLeaf,
    });
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.c === other.c;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.c);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<NLeaf>): NLeaf {
    return copyRecordWith<NLeaf>(this, overrides);
  }

  protected override $printMembers(): string {
    const base = super.$printMembers?.() ?? "";

    return `${base ? `${base}, ` : ""}C = ${this.c}`;
  }

  override toString(): string {
    return `NLeaf { ${this.$printMembers()} }`;
  }
}
