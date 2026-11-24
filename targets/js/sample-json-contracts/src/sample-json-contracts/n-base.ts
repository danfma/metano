/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";

let NBase$rt: ClassType<NBase> | undefined;

export abstract class NBase {
  constructor(readonly a: number) {}

  static get $runtimeType(): ClassType<NBase> {
    return (NBase$rt ??= {
      kind: "class",
      name: "NBase",
      fullName: "SampleJsonContracts.NBase",
      namespace: "SampleJsonContracts",
      ctor: NBase,
      interfaces: [],
      typeParameters: [],
      isAbstract: true,
      isValueType: false,
      test: (v: unknown): v is NBase => v instanceof NBase,
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

  with(overrides?: Partial<NBase>): NBase {
    return copyRecordWith<NBase>(this, overrides);
  }

  protected $printMembers(): string {
    return `A = ${this.a}`;
  }

  toString(): string {
    return `NBase { ${this.$printMembers()} }`;
  }
}
