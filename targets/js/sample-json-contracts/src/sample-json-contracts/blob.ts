/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";

let Blob$rt: ClassType<Blob> | undefined;

export class Blob {
  constructor(
    readonly data: number[],
    readonly maybe: number[] | null,
    readonly numbers: number[],
  ) {}

  static get $runtimeType(): ClassType<Blob> {
    return (Blob$rt ??= {
      kind: "class",
      name: "Blob",
      fullName: "SampleJsonContracts.Blob",
      namespace: "SampleJsonContracts",
      ctor: Blob,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is Blob => v instanceof Blob,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.data === other.data &&
      this.maybe === other.maybe &&
      this.numbers === other.numbers
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.data);
    hc.add(this.maybe);
    hc.add(this.numbers);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Blob>): Blob {
    return copyRecordWith<Blob>(this, overrides);
  }
}
