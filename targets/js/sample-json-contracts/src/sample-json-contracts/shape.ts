/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { PlainObjectType } from "metano-runtime";

export interface Shape {}

export function isShape(value: unknown): value is Shape {
  if (value == null || typeof value !== "object") {
    return false;
  }

  const v = value as any;

  return true;
}

export const Shape$runtimeType: PlainObjectType<Shape> = {
  kind: "plainObject",
  name: "Shape",
  fullName: "SampleJsonContracts.Shape",
  namespace: "SampleJsonContracts",
  guard: isShape,
  test: isShape,
};
