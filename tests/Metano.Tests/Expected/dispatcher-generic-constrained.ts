import { isInt32 } from "metano-runtime";

export class RefBox<T> {
  constructor(x: number);
  constructor(x: T);
  constructor(...args: unknown[]) {
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
}

import { isInt32 } from "metano-runtime";
import type { IShape } from "./i-shape";

export class ShapeBox<T extends IShape> {
  constructor(x: number);
  constructor(x: T);
  constructor(...args: unknown[]) {
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
}

import { isInt32 } from "metano-runtime";
import { Animal } from "./animal";

export class AnimalBox<T extends Animal> {
  constructor(x: number);
  constructor(x: T);
  constructor(...args: unknown[]) {
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
}

import { isInt32 } from "metano-runtime";

export class ValBox<T> {
  constructor(x: T, n: number);
  constructor(x: T);
  constructor(...args: unknown[]) {
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
}
