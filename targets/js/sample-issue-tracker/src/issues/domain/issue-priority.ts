export const IssuePriority = {
  Low: "low",
  Medium: "medium",
  High: "high",
  Urgent: "urgent",
} as const;

export type IssuePriority = typeof IssuePriority[keyof typeof IssuePriority];
