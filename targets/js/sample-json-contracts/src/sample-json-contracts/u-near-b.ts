/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import { UNearA } from "./u-near-a.js";

let UNearB$rt: ClassType<UNearB> | undefined;

export class UNearB extends UNearA {
  declare readonly c: number;

  constructor(a: number, b: number, c: number) {
    super(a, b);
    this.c = c;
  }

  static override get $runtimeType(): ClassType<UNearB> {
    return (UNearB$rt ??= {
      kind: "class",
      name: "UNearB",
      fullName: "SampleJsonContracts.UNearB",
      namespace: "SampleJsonContracts",
      ctor: UNearB,
      base: () => UNearA.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is UNearB => v instanceof UNearB,
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

  override with(overrides?: Partial<UNearB>): UNearB {
    return copyRecordWith<UNearB>(this, overrides);
  }

  protected override $printMembers(): string {
    const base = super.$printMembers?.() ?? "";

    return `${base ? `${base}, ` : ""}C = ${this.c}`;
  }

  override toString(): string {
    return `UNearB { ${this.$printMembers()} }`;
  }
}
