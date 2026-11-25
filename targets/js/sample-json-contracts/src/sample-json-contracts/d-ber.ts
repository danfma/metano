/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import { DBase } from "./d-base.js";

let DBer$rt: ClassType<DBer> | undefined;

export class DBer extends DBase {
  constructor(readonly w: number) {
    super();
  }

  static override get $runtimeType(): ClassType<DBer> {
    return (DBer$rt ??= {
      kind: "class",
      name: "DBer",
      fullName: "SampleJsonContracts.DBer",
      namespace: "SampleJsonContracts",
      ctor: DBer,
      base: () => DBase.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is DBer => v instanceof DBer,
    });
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.w === other.w;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.w);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<DBer>): DBer {
    return copyRecordWith<DBer>(this, overrides);
  }

  protected override $printMembers(): string {
    const base = super.$printMembers?.() ?? "";

    return `${base ? `${base}, ` : ""}W = ${this.w}`;
  }

  override toString(): string {
    return `DBer { ${this.$printMembers()} }`;
  }
}
