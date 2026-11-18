/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { PlainObjectType } from "metano-runtime";

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

export const Point3$runtimeType: PlainObjectType<Point3> = {
  kind: "plainObject",
  name: "Point3",
  fullName: "SampleJsonContracts.Point3",
  namespace: "SampleJsonContracts",
  guard: isPoint3,
  test: isPoint3,
};
