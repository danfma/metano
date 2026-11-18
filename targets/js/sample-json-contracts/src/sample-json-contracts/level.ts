/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { EnumType } from "metano-runtime";

export const Level = {
  Low: 0,
  High: 1,
} as const;

export type Level = (typeof Level)[keyof typeof Level];

export const Level$runtimeType: EnumType<Level> = {
  kind: "enum",
  name: "Level",
  fullName: "SampleJsonContracts.Level",
  namespace: "SampleJsonContracts",
  values: [0, 1],
  names: ["Low", "High"],
  isFlags: false,
  test: (v: unknown): v is Level => v === 0 || v === 1,
};
