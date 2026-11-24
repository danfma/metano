/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";

let UNear$rt: ClassType<UNear> | undefined;

export abstract class UNear {
  constructor(readonly a: number) {}

  static get $runtimeType(): ClassType<UNear> {
    return (UNear$rt ??= {
      kind: "class",
      name: "UNear",
      fullName: "SampleJsonContracts.UNear",
      namespace: "SampleJsonContracts",
      ctor: UNear,
      interfaces: [],
      typeParameters: [],
      isAbstract: true,
      isValueType: false,
      test: (v: unknown): v is UNear => v instanceof UNear,
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

  with(overrides?: Partial<UNear>): UNear {
    return copyRecordWith<UNear>(this, overrides);
  }

  protected $printMembers(): string {
    return `A = ${this.a}`;
  }

  toString(): string {
    return `UNear { ${this.$printMembers()} }`;
  }
}
