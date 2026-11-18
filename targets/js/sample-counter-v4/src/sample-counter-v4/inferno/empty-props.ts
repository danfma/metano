/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { PlainObjectType } from "metano-runtime";

export interface EmptyProps {}

export function isEmptyProps(value: unknown): value is EmptyProps {
  if (value == null || typeof value !== "object") {
    return false;
  }

  const v = value as any;

  return true;
}

export const EmptyProps$runtimeType: PlainObjectType<EmptyProps> = {
  kind: "plainObject",
  name: "EmptyProps",
  fullName: "SampleCounterV4.Inferno.EmptyProps",
  namespace: "SampleCounterV4.Inferno",
  guard: isEmptyProps,
  test: isEmptyProps,
};
