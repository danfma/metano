/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";

export class Blob {
  constructor(
    readonly data: number[],
    readonly maybe: number[] | null,
    readonly numbers: number[],
  ) {}

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
