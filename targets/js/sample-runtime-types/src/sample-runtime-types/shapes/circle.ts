/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { ClassType } from "metano-runtime";
import { IHasArea$runtimeType, type IHasArea } from "./i-has-area";
import { Shape } from "./shape";

let Circle$rt: ClassType<Circle> | undefined;

export class Circle extends Shape implements IHasArea {
  readonly radius: number = 1;

  constructor() {
    super();
  }

  static override get $runtimeType(): ClassType<Circle> {
    return (Circle$rt ??= {
      kind: "class",
      name: "Circle",
      fullName: "SampleRuntimeTypes.Shapes.Circle",
      namespace: "SampleRuntimeTypes.Shapes",
      ctor: Circle,
      base: () => Shape.$runtimeType,
      interfaces: [() => IHasArea$runtimeType],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new Circle(),
      test: (v: unknown): v is Circle => v instanceof Circle,
    });
  }

  get area(): number {
    return 3 * this.radius * this.radius;
  }
}
