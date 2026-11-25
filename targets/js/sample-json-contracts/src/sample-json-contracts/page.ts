/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import type { IReadOnlyList } from "metano-runtime/system/collections";

let Page$rt: ClassType<Page<unknown>> | undefined;

export class Page<T> {
  constructor(readonly items: IReadOnlyList<T>) {}

  static get $runtimeType(): ClassType<Page<unknown>> {
    return (Page$rt ??= {
      kind: "class",
      name: "Page`1",
      fullName: "SampleJsonContracts.Page`1",
      namespace: "SampleJsonContracts",
      ctor: Page,
      interfaces: [],
      typeParameters: ["T"],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is Page<unknown> => v instanceof Page,
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

  with(overrides?: Partial<Page<T>>): Page<T> {
    return copyRecordWith<Page<T>>(this, overrides);
  }

  toString(): string {
    return `Page { Items = ${this.items} }`;
  }
}
