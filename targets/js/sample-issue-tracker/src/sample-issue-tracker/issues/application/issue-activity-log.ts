/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import { addEntry, tryGetValue } from "metano-runtime";
import { linq, orderBy, sumInt64, toArray, where } from "metano-runtime/system/linq";
import type { IssueActivity } from "#/sample-issue-tracker/issues/domain/issue-activity";

export class IssueActivityLog {
  private readonly _activities: Map<bigint, IssueActivity> = new Map();

  constructor() {}

  get activities(): Map<bigint, IssueActivity> {
    return this._activities;
  }

  get count(): number {
    return this._activities.size;
  }

  record(activity: IssueActivity): IssueActivity {
    addEntry(this._activities, activity.id, activity);

    return activity;
  }

  find(id: bigint): IssueActivity | null {
    const [$found0, activity] = tryGetValue(this._activities, id, null);

    return $found0 ? activity : null;
  }

  remove(id: bigint): boolean {
    return this._activities.delete(id);
  }

  totalDurationTicks(): bigint {
    return linq(
      [...this._activities.values()],
      sumInt64((activity: IssueActivity) => activity.durationTicks ?? 0n),
    );
  }

  within(startTicks: bigint, endTicks: bigint): IssueActivity[] {
    return linq(
      [...this._activities.values()],
      where((activity: IssueActivity) => activity.occurredWithin(startTicks, endTicks)),
      orderBy((activity: IssueActivity) => activity.occurredAtTicks),
      toArray(),
    );
  }
}
