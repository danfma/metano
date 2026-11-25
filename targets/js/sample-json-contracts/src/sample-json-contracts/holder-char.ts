/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";

let HolderChar$rt: ClassType<HolderChar> | undefined;

export class HolderChar {
  constructor(readonly c: string) {}

  static get $runtimeType(): ClassType<HolderChar> {
    return (HolderChar$rt ??= {
      kind: "class",
      name: "HolderChar",
      fullName: "SampleJsonContracts.HolderChar",
      namespace: "SampleJsonContracts",
      ctor: HolderChar,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is HolderChar => v instanceof HolderChar,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.c === other.c
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.c);

    return hc.toHashCode();
  }

  with(overrides?: Partial<HolderChar>): HolderChar {
    return copyRecordWith<HolderChar>(this, overrides);
  }

  protected $printMembers(): string {
    return `C = ${this.c}`;
  }

  toString(): string {
    return `HolderChar { ${this.$printMembers()} }`;
  }
}
