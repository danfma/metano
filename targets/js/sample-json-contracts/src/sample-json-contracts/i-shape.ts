/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { implementsInterface, type InterfaceType } from "metano-runtime";

export interface IShape {}

export const IShape$runtimeType: InterfaceType<IShape> = {
  kind: "interface",
  name: "IShape",
  fullName: "SampleJsonContracts.IShape",
  namespace: "SampleJsonContracts",
  baseInterfaces: [],
  plainImplementers: [],
  test: (v: unknown): v is IShape => implementsInterface(v, IShape$runtimeType),
};
