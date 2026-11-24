/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { isInt32, type EnumType } from "metano-runtime";

export const Perms = {
  None: 0,
  Read: 1,
  Write: 2,
  Execute: 4,
} as const;

export type Perms = number & { readonly __enum?: "Perms" };

export const Perms$runtimeType: EnumType<Perms> = {
  kind: "enum",
  name: "Perms",
  fullName: "SampleJsonContracts.Perms",
  namespace: "SampleJsonContracts",
  values: [0, 1, 2, 4],
  names: ["None", "Read", "Write", "Execute"],
  isFlags: true,
  default: () => 0 as Perms,
  test: (v: unknown): v is Perms => isInt32(v),
};
