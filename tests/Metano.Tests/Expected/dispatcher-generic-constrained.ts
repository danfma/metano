import { isInt32 } from "metano-runtime";

export class RefBox<T> {
  protected static readonly byRefBoxT: unique symbol = Symbol("RefBox(T x)");

  protected static readonly byRefBoxInt32: unique symbol = Symbol("RefBox(int x)");

  constructor(overload: typeof RefBox.byRefBoxT, x: T);
  constructor(overload: typeof RefBox.byRefBoxInt32, x: number);
  constructor(x: number);
  constructor(x: T);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === RefBox.byRefBoxT) {
      const x = args[1] as T;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === RefBox.byRefBoxInt32) {
      const x = args[1] as number;

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const x = args[0] as number;

      return;
    }

    if (args.length === 1 && (typeof args[0] === "object" || typeof args[0] === "function" || typeof args[0] === "string")) {
      const x = args[0] as T;

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

import { isInt32 } from "metano-runtime";
import type { IShape } from "./i-shape";

export class ShapeBox<T extends IShape> {
  protected static readonly byShapeBoxT: unique symbol = Symbol("ShapeBox(T x)");

  protected static readonly byShapeBoxInt32: unique symbol = Symbol("ShapeBox(int x)");

  constructor(overload: typeof ShapeBox.byShapeBoxT, x: T);
  constructor(overload: typeof ShapeBox.byShapeBoxInt32, x: number);
  constructor(x: number);
  constructor(x: T);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === ShapeBox.byShapeBoxT) {
      const x = args[1] as T;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === ShapeBox.byShapeBoxInt32) {
      const x = args[1] as number;

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const x = args[0] as number;

      return;
    }

    if (args.length === 1 && typeof args[0] === "object") {
      const x = args[0] as T;

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
  protected static readonly byAnimalBoxT: unique symbol = Symbol("AnimalBox(T x)");

  protected static readonly byAnimalBoxInt32: unique symbol = Symbol("AnimalBox(int x)");

  constructor(overload: typeof AnimalBox.byAnimalBoxT, x: T);
  constructor(overload: typeof AnimalBox.byAnimalBoxInt32, x: number);
  constructor(x: number);
  constructor(x: T);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === AnimalBox.byAnimalBoxT) {
      const x = args[1] as T;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === AnimalBox.byAnimalBoxInt32) {
      const x = args[1] as number;

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const x = args[0] as number;

      return;
    }

    if (args.length === 1 && args[0] instanceof Animal) {
      const x = args[0] as T;

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
  protected static readonly byValBoxX: unique symbol = Symbol("ValBox(T x)");

  protected static readonly byValBoxXN: unique symbol = Symbol("ValBox(T x, int n)");

  constructor(overload: typeof ValBox.byValBoxX, x: T);
  constructor(overload: typeof ValBox.byValBoxXN, x: T, n: number);
  constructor(x: T, n: number);
  constructor(x: T);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === ValBox.byValBoxX) {
      const x = args[1] as T;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === ValBox.byValBoxXN) {
      const x = args[1] as T;
      const n = args[2] as number;

      return;
    }

    if (args.length === 2 && isInt32(args[1])) {
      const x = args[0] as T;
      const n = args[1] as number;

      return;
    }

    if (args.length === 1) {
      const x = args[0] as T;

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
