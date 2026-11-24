/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import { IBase } from "./i-base";

let IS$rt: ClassType<IS> | undefined;

export class IS extends IBase {
  constructor(readonly y: number) {
    super();
  }

  static override get $runtimeType(): ClassType<IS> {
    return (IS$rt ??= {
      kind: "class",
      name: "IS",
      fullName: "SampleJsonContracts.IS",
      namespace: "SampleJsonContracts",
      ctor: IS,
      base: () => IBase.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is IS => v instanceof IS,
    });
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.y === other.y;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.y);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<IS>): IS {
    return copyRecordWith<IS>(this, overrides);
  }

  protected override $printMembers(): string {
    const base = super.$printMembers?.() ?? "";

    return `${base ? `${base}, ` : ""}Y = ${this.y}`;
  }

  override toString(): string {
    return `IS { ${this.$printMembers()} }`;
  }
}
