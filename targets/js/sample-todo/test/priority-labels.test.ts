import { describe, expect, test } from "bun:test";

import { Priority } from "#/sample-todo/priority";
import { PriorityLabels } from "#/sample-todo/priority-labels";

describe("PriorityLabels", () => {
  test("the static constructor fills the static readonly table", () => {
    expect(PriorityLabels.labels.get(Priority.Low)).toBe("Low");
    expect(PriorityLabels.labels.get(Priority.High)).toBe("High");
    expect(PriorityLabels.labels.count).toBe(3);
  });
});
