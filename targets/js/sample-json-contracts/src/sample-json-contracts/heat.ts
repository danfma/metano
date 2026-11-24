/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { isInt32, type EnumType } from "metano-runtime";

export const Heat = {
  Cool: 0,
  Warm: 1,
} as const;

export type Heat = (typeof Heat)[keyof typeof Heat];

export const Heat$runtimeType: EnumType<Heat> = {
  kind: "enum",
  name: "Heat",
  fullName: "SampleJsonContracts.Heat",
  namespace: "SampleJsonContracts",
  values: [0, 1],
  names: ["Cool", "Warm"],
  isFlags: false,
  default: () => 0,
  test: (v: unknown): v is Heat => isInt32(v),
};
