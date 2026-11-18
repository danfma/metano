/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, valueEquals, type ClassType } from "metano-runtime";
import type { EpisodeSummary } from "./episode-summary";
import type { Page } from "./page";

let Feed$rt: ClassType<Feed> | undefined;

export class Feed {
  constructor(readonly latest: Page<EpisodeSummary>) {}

  static get $runtimeType(): ClassType<Feed> {
    return (Feed$rt ??= {
      kind: "class",
      name: "Feed",
      fullName: "SampleJsonContracts.Feed",
      namespace: "SampleJsonContracts",
      ctor: Feed,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is Feed => v instanceof Feed,
    });
  }

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
