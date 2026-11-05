/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";

export class EpisodeSummary {
  constructor(
    readonly id: string,
    readonly title: string,
  ) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.id === other.id &&
      this.title === other.title
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.id);
    hc.add(this.title);

    return hc.toHashCode();
  }

  with(overrides?: Partial<EpisodeSummary>): EpisodeSummary {
    return copyRecordWith<EpisodeSummary>(this, overrides);
  }
}
