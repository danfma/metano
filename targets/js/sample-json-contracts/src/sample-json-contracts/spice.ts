/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { EnumType } from "metano-runtime";

export const Spice = {
  Mild: "Mild",
  Hot: "Hot",
} as const;

export type Spice = (typeof Spice)[keyof typeof Spice];

export const Spice$runtimeType: EnumType<Spice> = {
  kind: "stringEnum",
  name: "Spice",
  fullName: "SampleJsonContracts.Spice",
  namespace: "SampleJsonContracts",
  values: ["Mild", "Hot"],
  names: ["Mild", "Hot"],
  isFlags: false,
  default: () => "Mild",
  test: (v: unknown): v is Spice => v === "Mild" || v === "Hot",
};
