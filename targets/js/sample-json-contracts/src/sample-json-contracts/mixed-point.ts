/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { PlainObjectType } from "metano-runtime";

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

export const MixedPoint$runtimeType: PlainObjectType<MixedPoint> = {
  kind: "plainObject",
  name: "MixedPoint",
  fullName: "SampleJsonContracts.MixedPoint",
  namespace: "SampleJsonContracts",
  guard: isMixedPoint,
  test: isMixedPoint,
};
