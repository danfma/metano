import { Temporal } from "@js-temporal/polyfill";
import { dayNumber } from "@meta-sharp/runtime";
import { HashSet } from "@meta-sharp/runtime";
import type { IssueId } from "../../Issues/Domain/IssueId";
export class Sprint {
  constructor(readonly key: string, public name: string, public startDate: Temporal.PlainDate, public endDate: Temporal.PlainDate) { }

  private readonly _plannedIssues: HashSet<IssueId> = new HashSet();

  get plannedIssues(): Iterable<IssueId> {
    return this._plannedIssues;
  }

  get plannedCount(): number {
    return this._plannedIssues.size;
  }

  get durationDays(): number {
    return dayNumber(this.endDate) - dayNumber(this.startDate) + 1;
  }

  isActiveOn(date: Temporal.PlainDate): boolean {
    return date >= this.startDate && date <= this.endDate;
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
    this._plannedIssues.delete(issueId);
  }
}
