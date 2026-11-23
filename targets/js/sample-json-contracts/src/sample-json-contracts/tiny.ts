/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { isByte, type EnumType } from "metano-runtime";

export const Tiny = {
  Small: 1,
  Big: 2,
} as const;

export type Tiny = (typeof Tiny)[keyof typeof Tiny];

export const Tiny$runtimeType: EnumType<Tiny> = {
  kind: "enum",
  name: "Tiny",
  fullName: "SampleJsonContracts.Tiny",
  namespace: "SampleJsonContracts",
  values: [1, 2],
  names: ["Small", "Big"],
  isFlags: false,
  underlying: "Byte",
  default: () => 0 as Tiny,
  test: (v: unknown): v is Tiny => isByte(v),
};
