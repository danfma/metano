/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import {
  Dictionary,
  type IReadOnlyDictionary,
  type IReadOnlyList,
} from "metano-runtime/system/collections";
import type { ClassType } from "metano-runtime";
import { linq, orderBy, sumInt64, toList, where } from "metano-runtime/system/linq";
import type { IssueActivity } from "../domain/issue-activity.js";

let IssueActivityLog$rt: ClassType<IssueActivityLog> | undefined;

export class IssueActivityLog {
  private readonly _activities: Dictionary<bigint, IssueActivity> = new Dictionary<
    bigint,
    IssueActivity
  >();

  constructor() {}

  static get $runtimeType(): ClassType<IssueActivityLog> {
    return (IssueActivityLog$rt ??= {
      kind: "class",
      name: "IssueActivityLog",
      fullName: "SampleIssueTracker.Issues.Application.IssueActivityLog",
      namespace: "SampleIssueTracker.Issues.Application",
      ctor: IssueActivityLog,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new IssueActivityLog(),
      test: (v: unknown): v is IssueActivityLog => v instanceof IssueActivityLog,
    });
  }

  get activities(): IReadOnlyDictionary<bigint, IssueActivity> {
    return this._activities;
  }

  get count(): number {
    return this._activities.count;
  }

  record(activity: IssueActivity): IssueActivity {
    this._activities.add(activity.id, activity);

    return activity;
  }

  find(id: bigint): IssueActivity | null {
    const [$found0, activity] = this._activities.tryGetValue(id, null);

    return $found0 ? activity : null;
  }

  remove(id: bigint): boolean {
    return this._activities.remove(id);
  }

  totalDurationTicks(): bigint {
    return linq(
      this._activities.values,
      sumInt64((activity: IssueActivity) => activity.durationTicks ?? 0n),
    );
  }

  within(startTicks: bigint, endTicks: bigint): IReadOnlyList<IssueActivity> {
    return linq(
      this._activities.values,
      where((activity: IssueActivity) => activity.occurredWithin(startTicks, endTicks)),
      orderBy((activity: IssueActivity) => activity.occurredAtTicks),
      toList(),
    );
  }
}
