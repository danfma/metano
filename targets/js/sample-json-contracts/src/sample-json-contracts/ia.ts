/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import { IBase } from "./i-base.js";

let IA$rt: ClassType<IA> | undefined;

export class IA extends IBase {
  constructor(readonly x: number) {
    super();
  }

  static override get $runtimeType(): ClassType<IA> {
    return (IA$rt ??= {
      kind: "class",
      name: "IA",
      fullName: "SampleJsonContracts.IA",
      namespace: "SampleJsonContracts",
      ctor: IA,
      base: () => IBase.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is IA => v instanceof IA,
    });
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.x === other.x;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.x);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<IA>): IA {
    return copyRecordWith<IA>(this, overrides);
  }

  protected override $printMembers(): string {
    const base = super.$printMembers?.() ?? "";

    return `${base ? `${base}, ` : ""}X = ${this.x}`;
  }

  override toString(): string {
    return `IA { ${this.$printMembers()} }`;
  }
}
