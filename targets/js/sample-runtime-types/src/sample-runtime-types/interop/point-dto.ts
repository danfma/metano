/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { PlainObjectType } from "metano-runtime";

export interface PointDto {
  readonly x: number;
  readonly y: number;
}

export function isPointDto(value: unknown): value is PointDto {
  if (value == null || typeof value !== "object") {
    return false;
  }

  const v = value as any;

  return typeof v.x === "number" && typeof v.y === "number";
}

export const PointDto$runtimeType: PlainObjectType<PointDto> = {
  kind: "plainObject",
  name: "PointDto",
  fullName: "SampleRuntimeTypes.Interop.PointDto",
  namespace: "SampleRuntimeTypes.Interop",
  guard: isPointDto,
  test: isPointDto,
};
