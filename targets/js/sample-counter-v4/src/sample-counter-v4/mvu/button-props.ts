/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { PlainObjectType } from "metano-runtime";

export interface ButtonProps {
  readonly className: string | null;
  readonly onClick: () => void;
}

export function isButtonProps(value: unknown): value is ButtonProps {
  if (value == null || typeof value !== "object") {
    return false;
  }

  const v = value as any;

  return v.className == null || typeof v.className === "string";
}

export const ButtonProps$runtimeType: PlainObjectType<ButtonProps> = {
  kind: "plainObject",
  name: "ButtonProps",
  fullName: "SampleCounterV4.Mvu.ButtonProps",
  namespace: "SampleCounterV4.Mvu",
  guard: isButtonProps,
  test: isButtonProps,
};
