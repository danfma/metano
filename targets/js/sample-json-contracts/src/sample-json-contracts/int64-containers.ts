/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";

export class Int64Containers {
  constructor(
    readonly values: number[],
    readonly byName: Map<number, string>,
    readonly byMask: Map<number, string>,
  ) {}

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
