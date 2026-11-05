/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
export interface Point3 {
  readonly x: number;
  readonly y: number;
  readonly z: number;
}

export function isPoint3(value: unknown): value is Point3 {
  if (value == null || typeof value !== "object") {
    return false;
  }

  const v = value as any;

  return typeof v.y === "number" && typeof v.z === "number" && typeof v.x === "number";
}
