/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { implementsInterface, type InterfaceType } from "metano-runtime";

export interface IGauge {
  readonly level: number;
}

export const IGauge$runtimeType: InterfaceType<IGauge> = {
  kind: "interface",
  name: "IGauge",
  fullName: "SampleJsonContracts.IGauge",
  namespace: "SampleJsonContracts",
  baseInterfaces: [],
  plainImplementers: [],
  test: (v: unknown): v is IGauge => implementsInterface(v, IGauge$runtimeType),
};
