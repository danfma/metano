// receivers/blob.ts
export class Blob {
  constructor() { }
}

// receivers/circle.ts
import type { ClassType } from "metano-runtime";
import type { IHasArea } from "./i-has-area";
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
      fullName: "Receivers.Circle",
      namespace: "Receivers",
      ctor: Circle,
      base: () => Shape.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new Circle(),
      test: (v: unknown): v is Circle => v instanceof Circle,
    });
  }

  get area(): number {
    return 1;
  }
}

// receivers/color.ts
import type { EnumType } from "metano-runtime";

export const Color = {
  Red: 0,
  Green: 1,
} as const;

export type Color = typeof Color[keyof typeof Color];

export const Color$runtimeType: EnumType<Color> = {
  kind: "enum",
  name: "Color",
  fullName: "Receivers.Color",
  namespace: "Receivers",
  values: [0, 1],
  names: ["Red", "Green"],
  isFlags: false,
  test: (v: unknown): v is Color => v === 0 || v === 1,
};

// receivers/dto.ts
export interface Dto {
  readonly name: string;
}

// receivers/gadget.ts
import type { ClassType } from "metano-runtime";
import type { IHasArea } from "./i-has-area";

let Gadget$rt: ClassType<Gadget> | undefined;

export class Gadget implements IHasArea {
  constructor() { }

  static get $runtimeType(): ClassType<Gadget> {
    return (Gadget$rt ??= {
      kind: "class",
      name: "Gadget",
      fullName: "Receivers.Gadget",
      namespace: "Receivers",
      ctor: Gadget,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new Gadget(),
      test: (v: unknown): v is Gadget => v instanceof Gadget,
    });
  }

  get area(): number {
    return 2;
  }
}

// receivers/i-has-area.ts
export interface IHasArea {
  readonly area: number;
}

// receivers/index.ts
export { Blob } from "./blob";
export { Circle } from "./circle";
export { Color, Color$runtimeType } from "./color";
export type { Dto } from "./dto";
export { Gadget } from "./gadget";
export type { IHasArea } from "./i-has-area";
export { Point } from "./point";
export { Probes } from "./probes";
export { Shape } from "./shape";
export { Square } from "./square";

// receivers/point.ts
import { HashCode, type ClassType } from "metano-runtime";

let Point$rt: ClassType<Point> | undefined;

export class Point {
  x: number = 0;

  constructor() { }

  static get $runtimeType(): ClassType<Point> {
    return (Point$rt ??= {
      kind: "class",
      name: "Point",
      fullName: "Receivers.Point",
      namespace: "Receivers",
      ctor: Point,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: true,
      create: () => new Point(),
      test: (v: unknown): v is Point => v instanceof Point,
    });
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.x === other.x;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.x);

    return hc.toHashCode();
  }
}

// receivers/probes.ts
import { getRuntimeType, Int32Type } from "metano-runtime";
import type { Exception } from "metano-runtime/system/exceptions/exception";
import { Color$runtimeType, type Color } from "./color";
import type { IHasArea } from "./i-has-area";
import { Point } from "./point";
import type { Shape } from "./shape";

export class Probes {
  constructor() { }

  static ofObject(o: Object): string {
    return getRuntimeType(o).name;
  }

  static ofException(e: Exception): string {
    return getRuntimeType(e).name;
  }

  static ofArea(area: IHasArea): string {
    return getRuntimeType(area).name;
  }

  static ofShape<T extends Shape>(shape: T): string {
    return getRuntimeType(shape).name;
  }

  static ofAnything<T>(value: T): string {
    return getRuntimeType(value).name;
  }

  static ofPoint(point: Point): string {
    return Point.$runtimeType.name;
  }

  static ofColor(color: Color): string {
    return Color$runtimeType.name;
  }

  static ofInt(number: number): string {
    return Int32Type.name;
  }
}

// receivers/shape.ts
import type { ClassType } from "metano-runtime";

let Shape$rt: ClassType<Shape> | undefined;

export abstract class Shape {
  constructor() { }

  static get $runtimeType(): ClassType<Shape> {
    return (Shape$rt ??= {
      kind: "class",
      name: "Shape",
      fullName: "Receivers.Shape",
      namespace: "Receivers",
      ctor: Shape,
      interfaces: [],
      typeParameters: [],
      isAbstract: true,
      isValueType: false,
      test: (v: unknown): v is Shape => v instanceof Shape,
    });
  }
}

// receivers/square.ts
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
      fullName: "Receivers.Square",
      namespace: "Receivers",
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
