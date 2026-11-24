/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { publicJsonShape, type ClassType } from "metano-runtime";
import { OBase } from "./o-base.js";

let ODer$rt: ClassType<ODer> | undefined;

export class ODer extends OBase {
  readonly x: number = 0;
  private name$ODer_qoo6r7: string = "";

  constructor() {
    super();
  }

  static override get $runtimeType(): ClassType<ODer> {
    return (ODer$rt ??= {
      kind: "class",
      name: "ODer",
      fullName: "SampleJsonContracts.ODer",
      namespace: "SampleJsonContracts",
      ctor: ODer,
      base: () => OBase.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new ODer(),
      test: (v: unknown): v is ODer => v instanceof ODer,
    });
  }

  get name(): string {
    return this.name$ODer_qoo6r7;
  }

  set name(value: string) {
    this.name$ODer_qoo6r7 = value;
  }

  toJSON(): unknown {
    return publicJsonShape(this);
  }
}
