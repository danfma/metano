/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import { MixedBase } from "./mixed-base.js";

let MixedClass$rt: ClassType<MixedClass> | undefined;

export class MixedClass extends MixedBase {
  declare readonly w: number;

  constructor(w: number) {
    super();
    this.w = w;
  }

  static override get $runtimeType(): ClassType<MixedClass> {
    return (MixedClass$rt ??= {
      kind: "class",
      name: "MixedClass",
      fullName: "SampleJsonContracts.MixedClass",
      namespace: "SampleJsonContracts",
      ctor: MixedClass,
      base: () => MixedBase.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is MixedClass => v instanceof MixedClass,
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

  override with(overrides?: Partial<MixedClass>): MixedClass {
    return copyRecordWith<MixedClass>(this, overrides);
  }

  protected override $printMembers(): string {
    const base = super.$printMembers?.() ?? "";

    return `${base ? `${base}, ` : ""}W = ${this.w}`;
  }

  override toString(): string {
    return `MixedClass { ${this.$printMembers()} }`;
  }
}
