/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import {
  HashCode,
  copyRecordWith,
  defaultEquals,
  objectText,
  type ClassType,
} from "metano-runtime";

let GBase$rt: ClassType<GBase<unknown>> | undefined;

export abstract class GBase<T> {
  declare readonly value: T;

  constructor(value: T) {
    this.value = value;
  }

  static get $runtimeType(): ClassType<GBase<unknown>> {
    return (GBase$rt ??= {
      kind: "class",
      name: "GBase`1",
      fullName: "SampleJsonContracts.GBase`1",
      namespace: "SampleJsonContracts",
      ctor: GBase,
      interfaces: [],
      typeParameters: ["T"],
      isAbstract: true,
      isValueType: false,
      test: (v: unknown): v is GBase<unknown> => v instanceof GBase,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      defaultEquals(this.value, other.value)
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.value);

    return hc.toHashCode();
  }

  with(overrides?: Partial<GBase<T>>): GBase<T> {
    return copyRecordWith<GBase<T>>(this, overrides);
  }

  protected $printMembers(): string {
    return `Value = ${objectText(this.value ?? "")}`;
  }

  toString(): string {
    return `GBase { ${this.$printMembers()} }`;
  }
}
