/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";

export class HolderChar {
  constructor(readonly c: string) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.c === other.c
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.c);

    return hc.toHashCode();
  }

  with(overrides?: Partial<HolderChar>): HolderChar {
    return copyRecordWith<HolderChar>(this, overrides);
  }
}
