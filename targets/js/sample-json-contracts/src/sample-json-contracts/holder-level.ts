/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, enumText, type ClassType } from "metano-runtime";
import { Level$runtimeType, type Level } from "./level.js";

let HolderLevel$rt: ClassType<HolderLevel> | undefined;

export class HolderLevel {
  declare readonly level: Level;

  constructor(level: Level) {
    this.level = level;
  }

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

  protected $printMembers(): string {
    return `Level = ${enumText(this.level, Level$runtimeType)}`;
  }

  toString(): string {
    return `HolderLevel { ${this.$printMembers()} }`;
  }
}
