/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import { HashCode, copyRecordWith } from "metano-runtime";
import type { IssueActivity } from "./issue-activity";

export class ActivityFeed {
  constructor(readonly entries: Map<bigint, IssueActivity>) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.entries === other.entries
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.entries);

    return hc.toHashCode();
  }

  with(overrides?: Partial<ActivityFeed>): ActivityFeed {
    return copyRecordWith<ActivityFeed>(this, overrides);
  }
}
