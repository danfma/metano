/**
 * The hand-written consumer of a C# 15 closed family (spec 010 FR-C5, #738):
 * `JobStatus` is the union of its cases, so a value typed with the family
 * name narrows on the read-only `kind` discriminant with no cast. The type
 * checks run under `tsgo -b` (this file is in the project); the runtime
 * checks pin that `kind` is a prototype accessor outside equality, `with`
 * and JSON.
 */

import { describe, expect, test } from "bun:test";

import { JobCompleted } from "#/sample-json-contracts/job-completed";
import { JobError } from "#/sample-json-contracts/job-error";
import { isJobFinished, JobFinished } from "#/sample-json-contracts/job-finished";
import { JobQueued } from "#/sample-json-contracts/job-queued";
import { isJobRunning, JobRunning } from "#/sample-json-contracts/job-running";
import { assertJobStatus, isJobStatus, JobStatus } from "#/sample-json-contracts/job-status";
import { JobStatuses } from "#/sample-json-contracts/job-statuses";

const label = (status: JobStatus): string => {
  switch (status.kind) {
    case "JobQueued":
      return "queued";
    case "JobRunning":
      return `${status.percent}%`;
    case "JobCompleted":
      return `${status.seconds}s`;
    case "JobError":
      return status.error;
    default:
      return status satisfies never;
  }
};

const finishedLabel = (finished: JobFinished): string =>
  finished.kind === "JobCompleted" ? `${finished.seconds}s` : finished.error;

const missingCase = (status: JobStatus): string => {
  switch (status.kind) {
    case "JobQueued":
      return "queued";
    case "JobRunning":
      return "running";
    case "JobCompleted":
      return "done";
    default:
      // @ts-expect-error JobError is not handled, so `status` is not `never`
      return status satisfies never;
  }
};

describe("closed family union (FR-C5)", () => {
  const statuses: JobStatus[] = [
    new JobQueued(),
    new JobRunning(40),
    new JobCompleted(3),
    new JobError("disk"),
  ];

  test("narrows on kind", () => {
    expect(statuses.map(label)).toEqual(["queued", "40%", "3s", "disk"]);
    expect(finishedLabel(new JobError("x"))).toBe("x");
    expect(missingCase(new JobError("x"))).toBeInstanceOf(JobError);
  });

  test("kind is the public TS name, on the prototype", () => {
    expect(statuses.map((status) => status.kind)).toEqual([
      "JobQueued",
      "JobRunning",
      "JobCompleted",
      "JobError",
    ]);

    const running = new JobRunning(40);

    expect(Object.keys(running)).toEqual(["percent"]);
    expect(Object.hasOwn(running, "kind")).toBe(false);
    expect(JSON.stringify(running)).not.toContain("kind");
  });

  test("with, equality and hash ignore kind", () => {
    const running = new JobRunning(40);
    const copy = running.with({ kind: "JobQueued" } as unknown as Partial<JobRunning>);

    expect(copy.kind).toBe("JobRunning");
    expect(copy.equals(running)).toBe(true);
    expect(copy.hashCode()).toBe(running.hashCode());
    expect(running.equals(new JobRunning(41))).toBe(false);
  });

  test("the family name is still the runtime class", () => {
    expect(new JobCompleted(1)).toBeInstanceOf(JobStatus);
    expect(new JobCompleted(1)).toBeInstanceOf(JobFinished);
    expect(JobStatus.family).toBe("jobs");
    expect(JobStatuses.asFinished(new JobError("e"))?.kind).toBe("JobError");
    expect(JobStatuses.asFinished(new JobQueued())).toBeNull();
    expect(JobStatuses.describe(new JobRunning(5))).toBe("running 5");
    expect(new JobQueued().self()).toBeInstanceOf(JobQueued);
  });

  test("guards admit instances only (#757)", () => {
    const plain: unknown = { percent: 40 };

    expect(isJobStatus(plain)).toBe(false);
    expect(isJobRunning(plain)).toBe(false);
    expect(isJobFinished({ seconds: 3 })).toBe(false);
    expect(() => assertJobStatus(plain)).toThrow(TypeError);

    const value: unknown = new JobRunning(40);

    expect(isJobRunning(value)).toBe(true);
    expect(isJobFinished(value)).toBe(false);
    expect(isJobFinished(new JobError("e"))).toBe(true);

    if (isJobStatus(value)) {
      expect(label(value)).toBe("40%");
    }
  });
});
