/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { ClassType } from "metano-runtime";

let Shape$rt: ClassType<Shape> | undefined;

export abstract class Shape {
  constructor() {}

  static get $runtimeType(): ClassType<Shape> {
    return (Shape$rt ??= {
      kind: "class",
      name: "Shape",
      fullName: "SampleRuntimeTypes.Shapes.Shape",
      namespace: "SampleRuntimeTypes.Shapes",
      ctor: Shape,
      interfaces: [],
      typeParameters: [],
      isAbstract: true,
      isValueType: false,
      test: (v: unknown): v is Shape => v instanceof Shape,
    });
  }
}
