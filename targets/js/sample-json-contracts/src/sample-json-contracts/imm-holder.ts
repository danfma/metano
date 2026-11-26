/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import type { ImmutableArray } from "metano-runtime/system/collections";

let ImmHolder$rt: ClassType<ImmHolder> | undefined;

export class ImmHolder {
  declare readonly items: ImmutableArray<number>;

  constructor(items: ImmutableArray<number>) {
    this.items = items;
  }

  static get $runtimeType(): ClassType<ImmHolder> {
    return (ImmHolder$rt ??= {
      kind: "class",
      name: "ImmHolder",
      fullName: "SampleJsonContracts.ImmHolder",
      namespace: "SampleJsonContracts",
      ctor: ImmHolder,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is ImmHolder => v instanceof ImmHolder,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.items === other.items
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.items);

    return hc.toHashCode();
  }

  with(overrides?: Partial<ImmHolder>): ImmHolder {
    return copyRecordWith<ImmHolder>(this, overrides);
  }

  toString(): string {
    return `ImmHolder { Items = ${this.items} }`;
  }
}
