/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
export const Color = {
  Red: 0,
  Green: 1,
  Blue: 2,
} as const;

export type Color = (typeof Color)[keyof typeof Color];
