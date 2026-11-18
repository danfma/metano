/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { isInt32, type EnumType } from "metano-runtime";

export const Color = {
  Red: 0,
  Green: 1,
  Blue: 2,
} as const;

export type Color = (typeof Color)[keyof typeof Color];

export const Color$runtimeType: EnumType<Color> = {
  kind: "enum",
  name: "Color",
  fullName: "SampleJsonContracts.Color",
  namespace: "SampleJsonContracts",
  values: [0, 1, 2],
  names: ["Red", "Green", "Blue"],
  isFlags: false,
  test: (v: unknown): v is Color => isInt32(v),
};
