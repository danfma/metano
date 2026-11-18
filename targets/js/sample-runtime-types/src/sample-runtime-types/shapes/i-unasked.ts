/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { implementsInterface, type InterfaceType } from "metano-runtime";

export interface IUnasked {
  readonly note: string;
}

export const IUnasked$runtimeType: InterfaceType<IUnasked> = {
  kind: "interface",
  name: "IUnasked",
  fullName: "SampleRuntimeTypes.Shapes.IUnasked",
  namespace: "SampleRuntimeTypes.Shapes",
  baseInterfaces: [],
  plainImplementers: [],
  test: (v: unknown): v is IUnasked => implementsInterface(v, IUnasked$runtimeType),
};
