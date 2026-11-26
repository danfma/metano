import { formatDouble, isInt32, isString } from "metano-runtime";
import { Circle } from "./circle.js";
import type { Shape } from "./shape.js";
import { Square } from "./square.js";

export class Describer {
  private constructor() { }

  static describe(o: Object | null): string {
    let $c;

    switch (true) {
      case o == null:
        return "null";
      case o instanceof Circle && ($c = o, $c.r > 10):
        {
          const c = o;

          return "big circle " + formatDouble(c.r);
        }
      case o instanceof Circle:
        {
          const c = o;

          return "circle " + formatDouble(c.r);
        }
      case o instanceof Square:
      case isInt32(o):
        return "square or int";
      case isString(o) && o.length > 3:
        {
          const text = o;

          return "long " + text;
        }
      default:
        return "other";
    }
  }

  static classify(n: number): string {
    let result = "";

    switch (true) {
      case n === 0:
        result = "zero";
        break;
      default:
        result = "many";
        break;
      case n > 5 && n < 10:
        result = "mid";
        break;
      case n === 1:
      case n === 2:
        result = "small";
        break;
    }

    return result;
  }

  static ofCurrent(shapes: Shape[]): string {
    let $s;

    {
      const $subject = shapes[0]!;

      switch (true) {
        case $subject instanceof Square && ($s = $subject, $s.side > 2):
          {
            const s = $subject;

            return "big square " + formatDouble(s.side);
          }
        case $subject instanceof Circle:
          return "circle";
        default:
          return "other";
      }
    }
  }
}
