/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { EnumType } from "metano-runtime";

export const Tone = {
  Mild: "mild",
  Sharp: "Sharp",
} as const;

export type Tone = (typeof Tone)[keyof typeof Tone];

export const Tone$runtimeType: EnumType<Tone> = {
  kind: "stringEnum",
  name: "Tone",
  fullName: "SampleJsonContracts.Tone",
  namespace: "SampleJsonContracts",
  values: ["mild", "Sharp"],
  names: ["Mild", "Sharp"],
  isFlags: false,
  default: () => "mild",
  test: (v: unknown): v is Tone => v === "mild" || v === "Sharp",
};
