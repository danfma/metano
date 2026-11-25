/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";

let HolderInt$rt: ClassType<HolderInt> | undefined;

export class HolderInt {
  constructor(readonly n: number) {}

  static get $runtimeType(): ClassType<HolderInt> {
    return (HolderInt$rt ??= {
      kind: "class",
      name: "HolderInt",
      fullName: "SampleJsonContracts.HolderInt",
      namespace: "SampleJsonContracts",
      ctor: HolderInt,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is HolderInt => v instanceof HolderInt,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.n === other.n
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.n);

    return hc.toHashCode();
  }

  with(overrides?: Partial<HolderInt>): HolderInt {
    return copyRecordWith<HolderInt>(this, overrides);
  }

  protected $printMembers(): string {
    return `N = ${this.n}`;
  }

  toString(): string {
    return `HolderInt { ${this.$printMembers()} }`;
  }
}
