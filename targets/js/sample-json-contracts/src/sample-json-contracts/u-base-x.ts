/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import { UBaseH } from "./u-base-h";

let UBaseX$rt: ClassType<UBaseX> | undefined;

export class UBaseX extends UBaseH {
  constructor(
    a: number,
    readonly x: number,
  ) {
    super(a);
  }

  static override get $runtimeType(): ClassType<UBaseX> {
    return (UBaseX$rt ??= {
      kind: "class",
      name: "UBaseX",
      fullName: "SampleJsonContracts.UBaseX",
      namespace: "SampleJsonContracts",
      ctor: UBaseX,
      base: () => UBaseH.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is UBaseX => v instanceof UBaseX,
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

  override with(overrides?: Partial<UBaseX>): UBaseX {
    return copyRecordWith<UBaseX>(this, overrides);
  }

  protected override $printMembers(): string {
    const base = super.$printMembers?.() ?? "";

    return `${base ? `${base}, ` : ""}X = ${this.x}`;
  }

  override toString(): string {
    return `UBaseX { ${this.$printMembers()} }`;
  }
}
