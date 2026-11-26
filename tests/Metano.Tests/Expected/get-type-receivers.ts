// receivers/blob.ts
export class Blob {
}

// receivers/circle.ts
import type { ClassType } from "metano-runtime";
import type { IHasArea } from "./i-has-area.js";
import { Shape } from "./shape.js";

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
import { isInt32, type EnumType } from "metano-runtime";

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
  default: () => 0,
  test: (v: unknown): v is Color => isInt32(v),
};

// receivers/dto.ts
export interface Dto {
  readonly name: string;
}

// receivers/gadget.ts
import type { ClassType } from "metano-runtime";
import type { IHasArea } from "./i-has-area.js";

let Gadget$rt: ClassType<Gadget> | undefined;

export class Gadget implements IHasArea {
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
export { Blob } from "./blob.js";
export { Circle } from "./circle.js";
export { Color, Color$runtimeType } from "./color.js";
export type { Dto } from "./dto.js";
export { Gadget } from "./gadget.js";
export type { IHasArea } from "./i-has-area.js";
export { Point } from "./point.js";
export { Probes } from "./probes.js";
export { Shape } from "./shape.js";
export { Square } from "./square.js";

// receivers/point.ts
import { HashCode, type ClassType } from "metano-runtime";

let Point$rt: ClassType<Point> | undefined;

export class Point {
  declare x: number;

  constructor() {
    this.x = 0;
  }

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
      default: () => Point.$default(),
      create: () => Point.$default(),
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

  static $default(): Point {
    return Object.assign(Object.create(Point.prototype), { x: 0 });
  }
}

// receivers/probes.ts
import { getRuntimeType, Int32Type } from "metano-runtime";
import type { Exception } from "metano-runtime/system/exceptions/exception";
import { Color$runtimeType, type Color } from "./color.js";
import type { IHasArea } from "./i-has-area.js";
import { Point } from "./point.js";
import type { Shape } from "./shape.js";

export class Probes {
  private constructor() { }

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
import { Shape } from "./shape.js";

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
