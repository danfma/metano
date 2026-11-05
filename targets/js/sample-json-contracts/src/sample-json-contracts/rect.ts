/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
export interface Rect {
  readonly w: number;
  readonly h: number;
}

export function isRect(value: unknown): value is Rect {
  if (value == null || typeof value !== "object") {
    return false;
  }

  const v = value as any;

  return typeof v.w === "number" && typeof v.h === "number";
}
