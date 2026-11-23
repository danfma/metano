import { isInt32 } from "metano-runtime";

export class RefBox<T> {
  protected static readonly byRefBoxT = /* @__PURE__ */ Symbol("RefBox(T x)");
  protected static readonly byRefBoxInt32 = /* @__PURE__ */ Symbol("RefBox(int x)");

  constructor(overload: typeof RefBox.byRefBoxT, x: T);
  constructor(overload: typeof RefBox.byRefBoxInt32, x: number);
  constructor(x: number);
  constructor(x: T);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === RefBox.byRefBoxT) {
      return;
    }

    if (typeof args[0] === "symbol" && args[0] === RefBox.byRefBoxInt32) {
      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      return;
    }

    if (args.length === 1 && (typeof args[0] === "object" || typeof args[0] === "function" || typeof args[0] === "string")) {
      return;
    }

    throw new Error("No matching constructor");
  }

  static fromT<T>(x: T): RefBox<T> {
    return new RefBox(RefBox.byRefBoxT, x);
  }

  static fromInt32<T>(x: number): RefBox<T> {
    return new RefBox(RefBox.byRefBoxInt32, x);
  }
}

import { isInt32, isType } from "metano-runtime";
import { IShape$runtimeType, type IShape } from "./i-shape";

export class ShapeBox<T extends IShape> {
  protected static readonly byShapeBoxT = /* @__PURE__ */ Symbol("ShapeBox(T x)");
  protected static readonly byShapeBoxInt32 = /* @__PURE__ */ Symbol("ShapeBox(int x)");

  constructor(overload: typeof ShapeBox.byShapeBoxT, x: T);
  constructor(overload: typeof ShapeBox.byShapeBoxInt32, x: number);
  constructor(x: number);
  constructor(x: T);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === ShapeBox.byShapeBoxT) {
      return;
    }

    if (typeof args[0] === "symbol" && args[0] === ShapeBox.byShapeBoxInt32) {
      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      return;
    }

    if (args.length === 1 && isType(args[0], IShape$runtimeType)) {
      return;
    }

    throw new Error("No matching constructor");
  }

  static fromT<T extends IShape>(x: T): ShapeBox<T> {
    return new ShapeBox(ShapeBox.byShapeBoxT, x);
  }

  static fromInt32<T extends IShape>(x: number): ShapeBox<T> {
    return new ShapeBox(ShapeBox.byShapeBoxInt32, x);
  }
}

import { isInt32 } from "metano-runtime";
import { Animal } from "./animal";

export class AnimalBox<T extends Animal> {
  protected static readonly byAnimalBoxT = /* @__PURE__ */ Symbol("AnimalBox(T x)");
  protected static readonly byAnimalBoxInt32 = /* @__PURE__ */ Symbol("AnimalBox(int x)");

  constructor(overload: typeof AnimalBox.byAnimalBoxT, x: T);
  constructor(overload: typeof AnimalBox.byAnimalBoxInt32, x: number);
  constructor(x: number);
  constructor(x: T);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === AnimalBox.byAnimalBoxT) {
      return;
    }

    if (typeof args[0] === "symbol" && args[0] === AnimalBox.byAnimalBoxInt32) {
      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      return;
    }

    if (args.length === 1 && args[0] instanceof Animal) {
      return;
    }

    throw new Error("No matching constructor");
  }

  static fromT<T extends Animal>(x: T): AnimalBox<T> {
    return new AnimalBox(AnimalBox.byAnimalBoxT, x);
  }

  static fromInt32<T extends Animal>(x: number): AnimalBox<T> {
    return new AnimalBox(AnimalBox.byAnimalBoxInt32, x);
  }
}

import { isInt32 } from "metano-runtime";

export class ValBox<T> {
  protected static readonly byValBoxX = /* @__PURE__ */ Symbol("ValBox(T x)");
  protected static readonly byValBoxXN = /* @__PURE__ */ Symbol("ValBox(T x, int n)");

  constructor(overload: typeof ValBox.byValBoxX, x: T);
  constructor(overload: typeof ValBox.byValBoxXN, x: T, n: number);
  constructor(x: T, n: number);
  constructor(x: T);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === ValBox.byValBoxX) {
      return;
    }

    if (typeof args[0] === "symbol" && args[0] === ValBox.byValBoxXN) {
      return;
    }

    if (args.length === 2 && isInt32(args[1])) {
      return;
    }

    if (args.length === 1) {
      return;
    }

    throw new Error("No matching constructor");
  }

  static fromX<T>(x: T): ValBox<T> {
    return new ValBox(ValBox.byValBoxX, x);
  }

  static fromXN<T>(x: T, n: number): ValBox<T> {
    return new ValBox(ValBox.byValBoxXN, x, n);
  }
}
