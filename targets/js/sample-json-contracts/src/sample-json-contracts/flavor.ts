/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { EnumType } from "metano-runtime";

export const Flavor = {
  Sweet: "Sweet",
  Salty: "Salty",
} as const;

export type Flavor = (typeof Flavor)[keyof typeof Flavor];

export const Flavor$runtimeType: EnumType<Flavor> = {
  kind: "stringEnum",
  name: "Flavor",
  fullName: "SampleJsonContracts.Flavor",
  namespace: "SampleJsonContracts",
  values: ["Sweet", "Salty"],
  names: ["Sweet", "Salty"],
  isFlags: false,
  default: () => "Sweet",
  test: (v: unknown): v is Flavor => v === "Sweet" || v === "Salty",
};
