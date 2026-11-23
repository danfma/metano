/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { isInt32, type EnumType } from "metano-runtime";

export const Currency = {
  Brl: 0,
  Usd: 1,
  Eur: 2,
} as const;

export type Currency = (typeof Currency)[keyof typeof Currency];

export const Currency$runtimeType: EnumType<Currency> = {
  kind: "enum",
  name: "Currency",
  fullName: "SampleOperatorOverloading.Currency",
  namespace: "SampleOperatorOverloading",
  values: [0, 1, 2],
  names: ["Brl", "Usd", "Eur"],
  isFlags: false,
  default: () => 0,
  test: (v: unknown): v is Currency => isInt32(v),
};
