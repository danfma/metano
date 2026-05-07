/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
export const IssuePriority = {
  Low: "low",
  Medium: "medium",
  High: "high",
  Urgent: "urgent",
} as const;

export type IssuePriority = typeof IssuePriority[keyof typeof IssuePriority];
