/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";

export class InitRec {
  readonly note: string | null = null;

  constructor(readonly a: number) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.a === other.a &&
      this.note === other.note
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.a);
    hc.add(this.note);

    return hc.toHashCode();
  }

  with(overrides?: Partial<InitRec>): InitRec {
    return copyRecordWith<InitRec>(this, overrides);
  }
}
