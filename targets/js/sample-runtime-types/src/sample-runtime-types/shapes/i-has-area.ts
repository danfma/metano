/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { implementsInterface, type InterfaceType } from "metano-runtime";
import { AreaDto$runtimeType } from "./area-dto.js";

export interface IHasArea {
  readonly area: number;
}

export const IHasArea$runtimeType: InterfaceType<IHasArea> = {
  kind: "interface",
  name: "IHasArea",
  fullName: "SampleRuntimeTypes.Shapes.IHasArea",
  namespace: "SampleRuntimeTypes.Shapes",
  baseInterfaces: [],
  plainImplementers: [() => AreaDto$runtimeType],
  test: (v: unknown): v is IHasArea => implementsInterface(v, IHasArea$runtimeType),
};
