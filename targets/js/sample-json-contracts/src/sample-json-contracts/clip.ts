/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import {
  HashCode,
  copyRecordWith,
  formatDouble,
  valueEquals,
  type ClassType,
} from "metano-runtime";

let Clip$rt: ClassType<Clip> | undefined;

export abstract class Clip {
  declare readonly seconds: number;

  constructor(seconds: number) {
    this.seconds = seconds;
  }

  static get $runtimeType(): ClassType<Clip> {
    return (Clip$rt ??= {
      kind: "class",
      name: "Clip",
      fullName: "SampleJsonContracts.Clip",
      namespace: "SampleJsonContracts",
      ctor: Clip,
      interfaces: [],
      typeParameters: [],
      isAbstract: true,
      isValueType: false,
      test: (v: unknown): v is Clip => v instanceof Clip,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      valueEquals(this.seconds, other.seconds)
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.seconds);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Clip>): Clip {
    return copyRecordWith<Clip>(this, overrides);
  }

  protected $printMembers(): string {
    return `Seconds = ${formatDouble(this.seconds)}`;
  }

  toString(): string {
    return `Clip { ${this.$printMembers()} }`;
  }
}
