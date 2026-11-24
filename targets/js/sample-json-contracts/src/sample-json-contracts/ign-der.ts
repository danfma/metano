/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import { IgnBase } from "./ign-base.js";

let IgnDer$rt: ClassType<IgnDer> | undefined;

export class IgnDer extends IgnBase {
  constructor(
    v: number,
    readonly w: number,
  ) {
    super(v);
  }

  static override get $runtimeType(): ClassType<IgnDer> {
    return (IgnDer$rt ??= {
      kind: "class",
      name: "IgnDer",
      fullName: "SampleJsonContracts.IgnDer",
      namespace: "SampleJsonContracts",
      ctor: IgnDer,
      base: () => IgnBase.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is IgnDer => v instanceof IgnDer,
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

  override with(overrides?: Partial<IgnDer>): IgnDer {
    return copyRecordWith<IgnDer>(this, overrides);
  }

  protected override $printMembers(): string {
    const base = super.$printMembers?.() ?? "";

    return `${base ? `${base}, ` : ""}W = ${this.w}`;
  }

  override toString(): string {
    return `IgnDer { ${this.$printMembers()} }`;
  }
}
