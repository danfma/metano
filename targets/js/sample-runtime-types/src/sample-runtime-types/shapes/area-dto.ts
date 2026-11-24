/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { PlainObjectType } from "metano-runtime";
import type { IHasArea } from "./i-has-area.js";

export interface AreaDto extends IHasArea {
  readonly area: number;
  readonly unit: string;
}

export function isAreaDto(value: unknown): value is AreaDto {
  if (value == null || typeof value !== "object") {
    return false;
  }

  const v = value as any;

  return typeof v.area === "number" && typeof v.unit === "string";
}

export const AreaDto$runtimeType: PlainObjectType<AreaDto> = {
  kind: "plainObject",
  name: "AreaDto",
  fullName: "SampleRuntimeTypes.Shapes.AreaDto",
  namespace: "SampleRuntimeTypes.Shapes",
  guard: isAreaDto,
  test: isAreaDto,
};
