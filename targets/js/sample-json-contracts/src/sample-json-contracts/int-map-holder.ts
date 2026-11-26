/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import type { Dictionary } from "metano-runtime/system/collections";

let IntMapHolder$rt: ClassType<IntMapHolder> | undefined;

export class IntMapHolder {
  declare readonly byKey: Dictionary<number, string>;

  constructor(byKey: Dictionary<number, string>) {
    this.byKey = byKey;
  }

  static get $runtimeType(): ClassType<IntMapHolder> {
    return (IntMapHolder$rt ??= {
      kind: "class",
      name: "IntMapHolder",
      fullName: "SampleJsonContracts.IntMapHolder",
      namespace: "SampleJsonContracts",
      ctor: IntMapHolder,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is IntMapHolder => v instanceof IntMapHolder,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.byKey === other.byKey
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.byKey);

    return hc.toHashCode();
  }

  with(overrides?: Partial<IntMapHolder>): IntMapHolder {
    return copyRecordWith<IntMapHolder>(this, overrides);
  }

  toString(): string {
    return `IntMapHolder { ByKey = ${this.byKey} }`;
  }
}
