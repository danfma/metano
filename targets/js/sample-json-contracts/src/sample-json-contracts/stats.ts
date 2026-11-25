/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import {
  HashCode,
  copyRecordWith,
  formatDouble,
  valueEquals,
  type ClassType,
} from "metano-runtime";

let Stats$rt: ClassType<Stats> | undefined;

export class Stats {
  declare readonly count: number;
  declare readonly ratio: number;
  declare readonly userName: string;

  constructor(count: number, ratio: number, userName: string) {
    this.count = count;
    this.ratio = ratio;
    this.userName = userName;
  }

  static get $runtimeType(): ClassType<Stats> {
    return (Stats$rt ??= {
      kind: "class",
      name: "Stats",
      fullName: "SampleJsonContracts.Stats",
      namespace: "SampleJsonContracts",
      ctor: Stats,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is Stats => v instanceof Stats,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.count === other.count &&
      valueEquals(this.ratio, other.ratio) &&
      this.userName === other.userName
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.count);
    hc.add(this.ratio);
    hc.add(this.userName);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Stats>): Stats {
    return copyRecordWith<Stats>(this, overrides);
  }

  toString(): string {
    return `Stats { Count = ${this.count}, Ratio = ${formatDouble(this.ratio)}, UserName = ${this.userName} }`;
  }
}
