/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";

let IgnBase$rt: ClassType<IgnBase> | undefined;

export class IgnBase {
  declare readonly v: number;

  constructor(v: number) {
    this.v = v;
  }

  static get $runtimeType(): ClassType<IgnBase> {
    return (IgnBase$rt ??= {
      kind: "class",
      name: "IgnBase",
      fullName: "SampleJsonContracts.IgnBase",
      namespace: "SampleJsonContracts",
      ctor: IgnBase,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is IgnBase => v instanceof IgnBase,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.v === other.v
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.v);

    return hc.toHashCode();
  }

  with(overrides?: Partial<IgnBase>): IgnBase {
    return copyRecordWith<IgnBase>(this, overrides);
  }

  protected $printMembers(): string {
    return `V = ${this.v}`;
  }

  toString(): string {
    return `IgnBase { ${this.$printMembers()} }`;
  }
}
