import { isInt32, isString } from "metano-runtime";
import { Circle } from "./circle.js";

export class Patterns {
  constructor() { }

  classify(d: any): number {
    let c: Circle;

    if (d instanceof Circle && (c = d, true)) {
      return 1;
    }

    if (d == null) {
      return 0;
    }

    const circle = d instanceof Circle ? d : null;

    return (($s) => {
      if (isInt32($s)) {
        const n = $s;

        return n;
      }

      if (isString($s)) {
        const s = $s;

        return s.length;
      }

      return -1;
    })(d);
  }
}
