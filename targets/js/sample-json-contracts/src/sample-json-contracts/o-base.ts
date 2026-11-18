/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { ClassType } from "metano-runtime";

let OBase$rt: ClassType<OBase> | undefined;

export abstract class OBase {
  readonly base: number = 0;
  abstract readonly name: string;

  constructor() {}

  static get $runtimeType(): ClassType<OBase> {
    return (OBase$rt ??= {
      kind: "class",
      name: "OBase",
      fullName: "SampleJsonContracts.OBase",
      namespace: "SampleJsonContracts",
      ctor: OBase,
      interfaces: [],
      typeParameters: [],
      isAbstract: true,
      isValueType: false,
      test: (v: unknown): v is OBase => v instanceof OBase,
    });
  }
}
