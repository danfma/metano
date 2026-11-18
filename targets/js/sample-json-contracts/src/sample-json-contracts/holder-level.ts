/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import type { Level } from "./level";

let HolderLevel$rt: ClassType<HolderLevel> | undefined;

export class HolderLevel {
  constructor(readonly level: Level) {}

  static get $runtimeType(): ClassType<HolderLevel> {
    return (HolderLevel$rt ??= {
      kind: "class",
      name: "HolderLevel",
      fullName: "SampleJsonContracts.HolderLevel",
      namespace: "SampleJsonContracts",
      ctor: HolderLevel,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is HolderLevel => v instanceof HolderLevel,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.level === other.level
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.level);

    return hc.toHashCode();
  }

  with(overrides?: Partial<HolderLevel>): HolderLevel {
    return copyRecordWith<HolderLevel>(this, overrides);
  }
}
