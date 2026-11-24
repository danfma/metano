/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";

let IgnAbs$rt: ClassType<IgnAbs> | undefined;

export abstract class IgnAbs {
  constructor(readonly v: number) {}

  static get $runtimeType(): ClassType<IgnAbs> {
    return (IgnAbs$rt ??= {
      kind: "class",
      name: "IgnAbs",
      fullName: "SampleJsonContracts.IgnAbs",
      namespace: "SampleJsonContracts",
      ctor: IgnAbs,
      interfaces: [],
      typeParameters: [],
      isAbstract: true,
      isValueType: false,
      test: (v: unknown): v is IgnAbs => v instanceof IgnAbs,
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

  with(overrides?: Partial<IgnAbs>): IgnAbs {
    return copyRecordWith<IgnAbs>(this, overrides);
  }

  protected $printMembers(): string {
    return `V = ${this.v}`;
  }

  toString(): string {
    return `IgnAbs { ${this.$printMembers()} }`;
  }
}
