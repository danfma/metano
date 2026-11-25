/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";

let OrdBase$rt: ClassType<OrdBase> | undefined;

export class OrdBase {
  constructor(
    readonly second: number,
    readonly first: number,
    readonly third: number,
  ) {}

  static get $runtimeType(): ClassType<OrdBase> {
    return (OrdBase$rt ??= {
      kind: "class",
      name: "OrdBase",
      fullName: "SampleJsonContracts.OrdBase",
      namespace: "SampleJsonContracts",
      ctor: OrdBase,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is OrdBase => v instanceof OrdBase,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.second === other.second &&
      this.first === other.first &&
      this.third === other.third
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.second);
    hc.add(this.first);
    hc.add(this.third);

    return hc.toHashCode();
  }

  with(overrides?: Partial<OrdBase>): OrdBase {
    return copyRecordWith<OrdBase>(this, overrides);
  }

  toString(): string {
    return `OrdBase { Second = ${this.second}, First = ${this.first}, Third = ${this.third} }`;
  }
}
