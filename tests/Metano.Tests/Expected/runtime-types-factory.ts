// geometry/blob.ts
export class Blob {
  constructor() { }

  get size(): number {
    return 1;
  }
}

// geometry/circle.ts
import type { ClassType } from "metano-runtime";
import { IHasArea$runtimeType, type IHasArea } from "./i-has-area";
import { Shape } from "./shape";

let Circle$rt: ClassType<Circle> | undefined;

export class Circle extends Shape implements IHasArea {
  constructor() {
    super();
  }

  static override get $runtimeType(): ClassType<Circle> {
    return (Circle$rt ??= {
      kind: "class",
      name: "Circle",
      fullName: "Geometry.Circle",
      namespace: "Geometry",
      ctor: Circle,
      base: () => Shape.$runtimeType,
      interfaces: [
        () => IHasArea$runtimeType,
      ],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new Circle(),
      test: (v: unknown): v is Circle => v instanceof Circle,
    });
  }

  get area(): number {
    return 3;
  }
}

// geometry/i-has-area.ts
import { implementsInterface, type InterfaceType } from "metano-runtime";

export interface IHasArea {
  readonly area: number;
}

export const IHasArea$runtimeType: InterfaceType<IHasArea> = {
  kind: "interface",
  name: "IHasArea",
  fullName: "Geometry.IHasArea",
  namespace: "Geometry",
  baseInterfaces: [],
  plainImplementers: [],
  test: (v: unknown): v is IHasArea => implementsInterface(v, IHasArea$runtimeType),
};

// geometry/index.ts
export { Blob } from "./blob";
export { Circle } from "./circle";
export { IHasArea$runtimeType } from "./i-has-area";
export type { IHasArea } from "./i-has-area";
export { Program } from "./program";
export { Shape } from "./shape";
export { Shapes } from "./shapes";
export { Square } from "./square";

// geometry/program.ts
import { castTo, formatBoolean, getRuntimeType, isType } from "metano-runtime";
import { Circle } from "./circle";
import { IHasArea$runtimeType } from "./i-has-area";
import type { Shape } from "./shape";
import { Shapes } from "./shapes";
import { Square } from "./square";

export class Program {
  constructor() { }

  static describe(kind: string): string {
    const s = Shapes.create(kind);
    const n = getRuntimeType(s).name;
    const same = getRuntimeType(s) === Circle.$runtimeType;
    const a = isType(s, IHasArea$runtimeType);
    const c = s instanceof Circle ? s : null;
    const k = (($s) => {
      if ($s instanceof Circle) {
        const c1 = $s;

        return 1;
      }

      if ($s instanceof Square) {
        const s1 = $s;

        return 2;
      }

      return 0;
    })(s);

    return `${n ?? ""} ${formatBoolean(same)} ${formatBoolean(a)} ${formatBoolean(c != null)} ${k}`;
  }

  static areaOf(s: Shape): number {
    return castTo<Circle>(s, Circle).area;
  }
}

// geometry/shape.ts
import type { ClassType } from "metano-runtime";

let Shape$rt: ClassType<Shape> | undefined;

export abstract class Shape {
  constructor() { }

  static get $runtimeType(): ClassType<Shape> {
    return (Shape$rt ??= {
      kind: "class",
      name: "Shape",
      fullName: "Geometry.Shape",
      namespace: "Geometry",
      ctor: Shape,
      interfaces: [],
      typeParameters: [],
      isAbstract: true,
      isValueType: false,
      test: (v: unknown): v is Shape => v instanceof Shape,
    });
  }
}

// geometry/shapes.ts
import { Circle } from "./circle";
import type { Shape } from "./shape";
import { Square } from "./square";

export class Shapes {
  constructor() { }

  static create(kind: string): Shape {
    return kind === "c" ? new Circle() : new Square();
  }
}

// geometry/square.ts
import type { ClassType } from "metano-runtime";
import { Shape } from "./shape";

let Square$rt: ClassType<Square> | undefined;

export class Square extends Shape {
  constructor() {
    super();
  }

  static override get $runtimeType(): ClassType<Square> {
    return (Square$rt ??= {
      kind: "class",
      name: "Square",
      fullName: "Geometry.Square",
      namespace: "Geometry",
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
