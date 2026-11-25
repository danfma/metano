/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";

let HBase$rt: ClassType<HBase> | undefined;

export abstract class HBase {
  constructor(readonly a: number) {}

  static get $runtimeType(): ClassType<HBase> {
    return (HBase$rt ??= {
      kind: "class",
      name: "HBase",
      fullName: "SampleJsonContracts.HBase",
      namespace: "SampleJsonContracts",
      ctor: HBase,
      interfaces: [],
      typeParameters: [],
      isAbstract: true,
      isValueType: false,
      test: (v: unknown): v is HBase => v instanceof HBase,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.a === other.a
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.a);

    return hc.toHashCode();
  }

  with(overrides?: Partial<HBase>): HBase {
    return copyRecordWith<HBase>(this, overrides);
  }

  protected $printMembers(): string {
    return `A = ${this.a}`;
  }

  toString(): string {
    return `HBase { ${this.$printMembers()} }`;
  }
}
