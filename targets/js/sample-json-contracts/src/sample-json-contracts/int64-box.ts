/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";

let Int64Box$rt: ClassType<Int64Box> | undefined;

export class Int64Box {
  constructor(
    readonly id: bigint,
    readonly mask: bigint,
    readonly parent: bigint | null,
  ) {}

  static get $runtimeType(): ClassType<Int64Box> {
    return (Int64Box$rt ??= {
      kind: "class",
      name: "Int64Box",
      fullName: "SampleJsonContracts.Int64Box",
      namespace: "SampleJsonContracts",
      ctor: Int64Box,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is Int64Box => v instanceof Int64Box,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.id === other.id &&
      this.mask === other.mask &&
      this.parent === other.parent
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.id);
    hc.add(this.mask);
    hc.add(this.parent);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Int64Box>): Int64Box {
    return copyRecordWith<Int64Box>(this, overrides);
  }

  toString(): string {
    return `Int64Box { Id = ${this.id}, Mask = ${this.mask}, Parent = ${this.parent ?? ""} }`;
  }
}
