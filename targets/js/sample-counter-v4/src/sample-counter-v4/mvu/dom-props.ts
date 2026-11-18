/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { PlainObjectType } from "metano-runtime";

export interface DomProps {
  readonly className?: string | null;
}

export function isDomProps(value: unknown): value is DomProps {
  if (value == null || typeof value !== "object") {
    return false;
  }

  const v = value as any;

  return v.className == null || typeof v.className === "string";
}

export const DomProps$runtimeType: PlainObjectType<DomProps> = {
  kind: "plainObject",
  name: "DomProps",
  fullName: "SampleCounterV4.Mvu.DomProps",
  namespace: "SampleCounterV4.Mvu",
  guard: isDomProps,
  test: isDomProps,
};
