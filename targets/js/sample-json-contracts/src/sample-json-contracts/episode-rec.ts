/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";
import type { Title } from "./title";

export class EpisodeRec {
  constructor(
    readonly id: string,
    readonly note: Title,
  ) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.id === other.id &&
      this.note === other.note
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.id);
    hc.add(this.note);

    return hc.toHashCode();
  }

  with(overrides?: Partial<EpisodeRec>): EpisodeRec {
    return copyRecordWith<EpisodeRec>(this, overrides);
  }
}
