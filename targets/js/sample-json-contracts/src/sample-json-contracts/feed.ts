/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, valueEquals } from "metano-runtime";
import type { EpisodeSummary } from "./episode-summary";
import type { Page } from "./page";

export class Feed {
  constructor(readonly latest: Page<EpisodeSummary>) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      valueEquals(this.latest, other.latest)
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.latest);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Feed>): Feed {
    return copyRecordWith<Feed>(this, overrides);
  }
}
