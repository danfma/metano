/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { EnumType } from "metano-runtime";

export const Priority = {
  Low: "low",
  Medium: "medium",
  High: "high",
} as const;

export type Priority = (typeof Priority)[keyof typeof Priority];

export const Priority$runtimeType: EnumType<Priority> = {
  kind: "stringEnum",
  name: "Priority",
  fullName: "SampleTodo.Priority",
  namespace: "SampleTodo",
  values: ["low", "medium", "high"],
  names: ["Low", "Medium", "High"],
  isFlags: false,
  test: (v: unknown): v is Priority => v === "low" || v === "medium" || v === "high",
};
