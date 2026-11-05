/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
export const Flavor = {
  Sweet: "Sweet",
  Salty: "Salty",
} as const;

export type Flavor = (typeof Flavor)[keyof typeof Flavor];
