/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import {
  HashCode,
  copyRecordWith,
  formatSingle,
  valueEquals,
  type ClassType,
} from "metano-runtime";

let SingleBox$rt: ClassType<SingleBox> | undefined;

export class SingleBox {
  constructor(
    readonly f: number,
    readonly g: number | null,
  ) {}

  static get $runtimeType(): ClassType<SingleBox> {
    return (SingleBox$rt ??= {
      kind: "class",
      name: "SingleBox",
      fullName: "SampleJsonContracts.SingleBox",
      namespace: "SampleJsonContracts",
      ctor: SingleBox,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is SingleBox => v instanceof SingleBox,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      valueEquals(this.f, other.f) &&
      valueEquals(this.g, other.g)
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.f);
    hc.add(this.g);

    return hc.toHashCode();
  }

  with(overrides?: Partial<SingleBox>): SingleBox {
    return copyRecordWith<SingleBox>(this, overrides);
  }

  toString(): string {
    return `SingleBox { F = ${formatSingle(this.f)}, G = ${formatSingle(this.g)} }`;
  }
}
