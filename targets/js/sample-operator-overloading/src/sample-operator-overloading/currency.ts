/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
export const Currency = {
  Brl: 0,
  Usd: 1,
  Eur: 2,
} as const;

export type Currency = (typeof Currency)[keyof typeof Currency];
