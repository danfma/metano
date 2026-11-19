/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import { Temporal } from "@js-temporal/polyfill";
import { HashSet, type IReadOnlyCollection } from "metano-runtime/system/collections";
import { dayNumber, type ClassType } from "metano-runtime";
import type { IssueId } from "#/sample-issue-tracker/issues/domain/issue-id";

let Sprint$rt: ClassType<Sprint> | undefined;

export class Sprint {
  private readonly _plannedIssues: HashSet<IssueId> = new HashSet<IssueId>();

  constructor(
    readonly key: string,
    public name: string,
    public startDate: Temporal.PlainDate,
    public endDate: Temporal.PlainDate,
  ) {}

  static get $runtimeType(): ClassType<Sprint> {
    return (Sprint$rt ??= {
      kind: "class",
      name: "Sprint",
      fullName: "SampleIssueTracker.Planning.Domain.Sprint",
      namespace: "SampleIssueTracker.Planning.Domain",
      ctor: Sprint,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is Sprint => v instanceof Sprint,
    });
  }

  get plannedIssues(): IReadOnlyCollection<IssueId> {
    return this._plannedIssues;
  }

  get plannedCount(): number {
    return this._plannedIssues.count;
  }

  get durationDays(): number {
    return (dayNumber(this.endDate) - dayNumber(this.startDate) + 1) | 0;
  }

  isActiveOn(date: Temporal.PlainDate): boolean {
    return (
      Temporal.PlainDate.compare(date, this.startDate) >= 0 &&
      Temporal.PlainDate.compare(date, this.endDate) <= 0
    );
  }

  rename(newName: string): void {
    this.name = newName;
  }

  reschedule(newStartDate: Temporal.PlainDate, newEndDate: Temporal.PlainDate): void {
    this.startDate = newStartDate;
    this.endDate = newEndDate;
  }

  plan(issueId: IssueId): void {
    this._plannedIssues.add(issueId);
  }

  unplan(issueId: IssueId): void {
    this._plannedIssues.remove(issueId);
  }
}
