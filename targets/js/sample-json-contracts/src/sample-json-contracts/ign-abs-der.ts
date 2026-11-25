/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import { IgnAbs } from "./ign-abs.js";

let IgnAbsDer$rt: ClassType<IgnAbsDer> | undefined;

export class IgnAbsDer extends IgnAbs {
  constructor(
    v: number,
    readonly w: number,
  ) {
    super(v);
  }

  static override get $runtimeType(): ClassType<IgnAbsDer> {
    return (IgnAbsDer$rt ??= {
      kind: "class",
      name: "IgnAbsDer",
      fullName: "SampleJsonContracts.IgnAbsDer",
      namespace: "SampleJsonContracts",
      ctor: IgnAbsDer,
      base: () => IgnAbs.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is IgnAbsDer => v instanceof IgnAbsDer,
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

  override with(overrides?: Partial<IgnAbsDer>): IgnAbsDer {
    return copyRecordWith<IgnAbsDer>(this, overrides);
  }

  protected override $printMembers(): string {
    const base = super.$printMembers?.() ?? "";

    return `${base ? `${base}, ` : ""}W = ${this.w}`;
  }

  override toString(): string {
    return `IgnAbsDer { ${this.$printMembers()} }`;
  }
}
