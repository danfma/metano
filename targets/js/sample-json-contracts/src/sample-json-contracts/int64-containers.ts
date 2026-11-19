/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import type { Dictionary } from "metano-runtime/system/collections";

let Int64Containers$rt: ClassType<Int64Containers> | undefined;

export class Int64Containers {
  constructor(
    readonly values: bigint[],
    readonly byName: Dictionary<bigint, string>,
    readonly byMask: Dictionary<bigint, string>,
  ) {}

  static get $runtimeType(): ClassType<Int64Containers> {
    return (Int64Containers$rt ??= {
      kind: "class",
      name: "Int64Containers",
      fullName: "SampleJsonContracts.Int64Containers",
      namespace: "SampleJsonContracts",
      ctor: Int64Containers,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is Int64Containers => v instanceof Int64Containers,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.values === other.values &&
      this.byName === other.byName &&
      this.byMask === other.byMask
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.values);
    hc.add(this.byName);
    hc.add(this.byMask);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Int64Containers>): Int64Containers {
    return copyRecordWith<Int64Containers>(this, overrides);
  }
}
