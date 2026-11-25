/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, valueEquals, type ClassType } from "metano-runtime";
import type { IReadOnlyList } from "metano-runtime/system/collections";
import type { Clip } from "./clip.js";

let Timeline$rt: ClassType<Timeline> | undefined;

export class Timeline {
  declare readonly name: string;
  declare readonly main: Clip;
  declare readonly clips: IReadOnlyList<Clip>;

  constructor(name: string, main: Clip, clips: IReadOnlyList<Clip>) {
    this.name = name;
    this.main = main;
    this.clips = clips;
  }

  static get $runtimeType(): ClassType<Timeline> {
    return (Timeline$rt ??= {
      kind: "class",
      name: "Timeline",
      fullName: "SampleJsonContracts.Timeline",
      namespace: "SampleJsonContracts",
      ctor: Timeline,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is Timeline => v instanceof Timeline,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.name === other.name &&
      valueEquals(this.main, other.main) &&
      this.clips === other.clips
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.name);
    hc.add(this.main);
    hc.add(this.clips);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Timeline>): Timeline {
    return copyRecordWith<Timeline>(this, overrides);
  }

  toString(): string {
    return `Timeline { Name = ${this.name}, Main = ${this.main}, Clips = ${this.clips} }`;
  }
}
