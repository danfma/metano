/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
export const Level = {
  Low: 0,
  High: 1,
} as const;

export type Level = (typeof Level)[keyof typeof Level];
