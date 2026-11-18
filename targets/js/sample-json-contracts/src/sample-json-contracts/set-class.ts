/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { ClassType } from "metano-runtime";

let SetClass$rt: ClassType<SetClass> | undefined;

export class SetClass {
  readonly n: number = 0;
  count: number = 5;

  constructor() {}

  static get $runtimeType(): ClassType<SetClass> {
    return (SetClass$rt ??= {
      kind: "class",
      name: "SetClass",
      fullName: "SampleJsonContracts.SetClass",
      namespace: "SampleJsonContracts",
      ctor: SetClass,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new SetClass(),
      test: (v: unknown): v is SetClass => v instanceof SetClass,
    });
  }
}
