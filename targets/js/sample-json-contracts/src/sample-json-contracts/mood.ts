/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { EnumType } from "metano-runtime";

export const Mood = {
  Calm: "Calm",
  Loud: "Loud",
} as const;

export type Mood = (typeof Mood)[keyof typeof Mood];

export const Mood$runtimeType: EnumType<Mood> = {
  kind: "stringEnum",
  name: "Mood",
  fullName: "SampleJsonContracts.Mood",
  namespace: "SampleJsonContracts",
  values: ["Calm", "Loud"],
  names: ["Calm", "Loud"],
  isFlags: false,
  default: () => "Calm",
  test: (v: unknown): v is Mood => v === "Calm" || v === "Loud",
};
