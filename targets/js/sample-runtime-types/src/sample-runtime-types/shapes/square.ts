/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { ClassType } from "metano-runtime";
import { Shape } from "./shape";

let Square$rt: ClassType<Square> | undefined;

export class Square extends Shape {
  readonly side: number = 1;

  constructor() {
    super();
  }

  static override get $runtimeType(): ClassType<Square> {
    return (Square$rt ??= {
      kind: "class",
      name: "Square",
      fullName: "SampleRuntimeTypes.Shapes.Square",
      namespace: "SampleRuntimeTypes.Shapes",
      ctor: Square,
      base: () => Shape.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new Square(),
      test: (v: unknown): v is Square => v instanceof Square,
    });
  }
}
