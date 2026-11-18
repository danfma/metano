/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { implementsInterface, type InterfaceType } from "metano-runtime";

export interface IShapeB {
  readonly area: number;
}

export const IShapeB$runtimeType: InterfaceType<IShapeB> = {
  kind: "interface",
  name: "IShapeB",
  fullName: "SampleJsonContracts.IShapeB",
  namespace: "SampleJsonContracts",
  baseInterfaces: [],
  plainImplementers: [],
  test: (v: unknown): v is IShapeB => implementsInterface(v, IShapeB$runtimeType),
};
