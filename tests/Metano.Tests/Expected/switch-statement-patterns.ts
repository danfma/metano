import { Circle } from "./circle";
import type { Shape } from "./shape";
import { Square } from "./square";

export class Describer {
  constructor() { }

  static describe(o: Object | null): string {
    switch (true) {
      case o == null:
        return "null";
      case o instanceof Circle && ((c) => c.r > 10)(o):
        {
          const c = o;

          return "big circle " + c.r;
        }
      case o instanceof Circle:
        {
          const c = o;

          return "circle " + c.r;
        }
      case o instanceof Square:
      case typeof o === "number":
        return "square or int";
      case typeof o === "string" && o.length > 3:
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
    {
      const $subject = shapes[0]!;

      switch (true) {
        case $subject instanceof Square && ((s) => s.side > 2)($subject):
          {
            const s = $subject;

            return "big square " + s.side;
          }
        case $subject instanceof Circle:
          return "circle";
        default:
          return "other";
      }
    }
  }
}
