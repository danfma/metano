import { Circle } from "./circle";
import { Node } from "./node";
import type { Shape } from "./shape";
import { Square } from "./square";

export class Geometry {
  constructor() { }

  static get radiusOf(): (arg: Shape) => number {
    return (s: Shape) => {
      let c: Circle;

      return s instanceof Circle && (c = s, true) ? c.r : 0;
    };
  }

  static area(shape: Shape): number {
    let c: Circle;

    if (shape instanceof Circle && (c = shape, true)) {
      return c.r * c.r * 3;
    }

    let s: Square;

    if (!(shape instanceof Square && (s = shape, true))) {
      return -1;
    }

    return s.side * s.side;
  }

  static radius(shape: Shape): number {
    let c: Circle;

    if (!(shape instanceof Circle && (c = shape, true))) {
      return -1;
    }

    return c.r;
  }

  static sameRadius(a: Shape, b: Shape): boolean {
    let x: Circle;
    let y: Circle;

    return a instanceof Circle && (x = a, true) && b instanceof Circle && (y = b, true) && x.r === y.r;
  }

  static walk(head: Node | null): number {
    let total = 0;
    let cursor = head;
    while (true) {
      let x: Node;

      if (!(cursor instanceof Node && (x = cursor, true))) {
        break;
      }

      total += x.value;
      cursor = x.next;
    }
    cursor = head;
    while (true) {
      let x: Node;

      if (!(cursor instanceof Node && (x = cursor, true))) {
        break;
      }

      total += x.value * 10;
      cursor = x.next;
    }

    return total;
  }

  static blocks(a: Object, b: Object): number {
    let total = 0;

    {
      let n: number;

      if (typeof a === "number" && (n = a, true)) {
        total += n;
      }
    }

    {
      let n: number;

      if (typeof b === "number" && (n = b, true)) {
        total += n * 100;
      }
    }

    return total;
  }

  static first(shapes: Shape[]): number {
    let $subject0: Shape;
    let c: Circle;

    return ($subject0 = shapes[0]!, true) && $subject0 instanceof Circle && (c = $subject0, true) ? c.r : 0;
  }

  static head(values: number[]): number {
    let first: number;
    let rest: number[] | null;

    return values.length >= 1 && (first = values[0]!, true) && (rest = values.slice(1), true) ? first + rest.length : -1;
  }
}
