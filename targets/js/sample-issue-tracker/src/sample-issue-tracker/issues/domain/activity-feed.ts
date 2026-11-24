/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import type { IReadOnlyDictionary } from "metano-runtime/system/collections";
import type { IssueActivity } from "./issue-activity.js";

let ActivityFeed$rt: ClassType<ActivityFeed> | undefined;

export class ActivityFeed {
  constructor(readonly entries: IReadOnlyDictionary<bigint, IssueActivity>) {}

  static get $runtimeType(): ClassType<ActivityFeed> {
    return (ActivityFeed$rt ??= {
      kind: "class",
      name: "ActivityFeed",
      fullName: "SampleIssueTracker.Issues.Domain.ActivityFeed",
      namespace: "SampleIssueTracker.Issues.Domain",
      ctor: ActivityFeed,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is ActivityFeed => v instanceof ActivityFeed,
    });
  }

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

  protected $printMembers(): string {
    return `Entries = ${this.entries ?? ""}`;
  }

  toString(): string {
    return `ActivityFeed { ${this.$printMembers()} }`;
  }
}
