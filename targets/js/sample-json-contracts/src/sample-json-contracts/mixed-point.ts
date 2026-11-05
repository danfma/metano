/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
export interface MixedPoint {
  readonly x: number;
}

export function isMixedPoint(value: unknown): value is MixedPoint {
  if (value == null || typeof value !== "object") {
    return false;
  }

  const v = value as any;

  return typeof v.x === "number";
}
