import type { ClassType, InterfaceType, PlainObjectType } from "#/system/reflection/index.ts";

/** Hand-written descriptors in the shape the compiler emits, shared by the reflection tests. */
export interface IShape {
  readonly area: number;
}

export interface IDrawable {
  readonly draw: () => void;
}

export interface IFlat {
  readonly flat: true;
}

export const IDrawable$rt: InterfaceType<IDrawable> = {
  kind: "interface",
  name: "IDrawable",
  fullName: "Geo.IDrawable",
  namespace: "Geo",
  baseInterfaces: [],
  plainImplementers: [],
  test: (v: unknown): v is IDrawable => false,
};

export const IShape$rt: InterfaceType<IShape> = {
  kind: "interface",
  name: "IShape",
  fullName: "Geo.IShape",
  namespace: "Geo",
  baseInterfaces: [() => IDrawable$rt],
  plainImplementers: [],
  test: (v: unknown): v is IShape => false,
};

export const FlatDto$rt: PlainObjectType<IFlat> = {
  kind: "plainObject",
  name: "FlatDto",
  fullName: "Geo.FlatDto",
  namespace: "Geo",
  guard: (v: unknown): v is IFlat => typeof v === "object" && v !== null && "flat" in v,
  test: (v: unknown): v is IFlat => typeof v === "object" && v !== null && "flat" in v,
};

export const IFlat$rt: InterfaceType<IFlat> = {
  kind: "interface",
  name: "IFlat",
  fullName: "Geo.IFlat",
  namespace: "Geo",
  baseInterfaces: [],
  plainImplementers: [() => FlatDto$rt],
  test: (v: unknown): v is IFlat => false,
};

let Shape$rt: ClassType<Shape> | undefined;
let Circle$rt: ClassType<Circle> | undefined;

export class Shape {
  static get $runtimeType(): ClassType<Shape> {
    return (Shape$rt ??= {
      kind: "class",
      name: "Shape",
      fullName: "Geo.Shape",
      namespace: "Geo",
      ctor: Shape,
      interfaces: [() => IShape$rt],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is Shape => v instanceof Shape,
    });
  }
}

export class Circle extends Shape {
  static override get $runtimeType(): ClassType<Circle> {
    return (Circle$rt ??= {
      kind: "class",
      name: "Circle",
      fullName: "Geo.Circle",
      namespace: "Geo",
      ctor: Circle,
      base: () => Shape.$runtimeType,
      interfaces: [() => IShape$rt],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is Circle => v instanceof Circle,
    });
  }
}

/** Derives from a described class without declaring a descriptor of its own. */
export class Undescribed extends Circle {}

/** A class the compiler never described. */
export class Stranger {}
