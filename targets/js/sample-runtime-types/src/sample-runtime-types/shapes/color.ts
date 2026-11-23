/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { isInt32, type EnumType } from "metano-runtime";

export const Color = {
  None: 0,
  Red: 1,
  Green: 2,
  Blue: 4,
} as const;

export type Color = number & { readonly __enum?: "Color" };

export const Color$runtimeType: EnumType<Color> = {
  kind: "enum",
  name: "Color",
  fullName: "SampleRuntimeTypes.Shapes.Color",
  namespace: "SampleRuntimeTypes.Shapes",
  values: [0, 1, 2, 4],
  names: ["None", "Red", "Green", "Blue"],
  isFlags: true,
  default: () => 0 as Color,
  test: (v: unknown): v is Color => isInt32(v),
};
