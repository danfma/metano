/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
export const Priority = {
  Low: "low",
  Medium: "medium",
  High: "high",
} as const;

export type Priority = typeof Priority[keyof typeof Priority];
