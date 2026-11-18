/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import type { Level } from "./level";

let LevelMapHolder$rt: ClassType<LevelMapHolder> | undefined;

export class LevelMapHolder {
  constructor(readonly byLevel: Map<Level, string>) {}

  static get $runtimeType(): ClassType<LevelMapHolder> {
    return (LevelMapHolder$rt ??= {
      kind: "class",
      name: "LevelMapHolder",
      fullName: "SampleJsonContracts.LevelMapHolder",
      namespace: "SampleJsonContracts",
      ctor: LevelMapHolder,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is LevelMapHolder => v instanceof LevelMapHolder,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.byLevel === other.byLevel
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.byLevel);

    return hc.toHashCode();
  }

  with(overrides?: Partial<LevelMapHolder>): LevelMapHolder {
    return copyRecordWith<LevelMapHolder>(this, overrides);
  }
}
