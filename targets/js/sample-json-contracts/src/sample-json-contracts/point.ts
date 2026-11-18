/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { PlainObjectType } from "metano-runtime";

export interface Point {
  readonly x: number;
}

export function isPoint(value: unknown): value is Point {
  if (value == null || typeof value !== "object") {
    return false;
  }

  const v = value as any;

  return typeof v.x === "number";
}

export const Point$runtimeType: PlainObjectType<Point> = {
  kind: "plainObject",
  name: "Point",
  fullName: "SampleJsonContracts.Point",
  namespace: "SampleJsonContracts",
  guard: isPoint,
  test: isPoint,
};
