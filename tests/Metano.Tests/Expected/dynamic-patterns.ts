import { Circle } from "./circle";

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
      if (typeof $s === "number") {
        const n = $s;

        return n;
      }

      if (typeof $s === "string") {
        const s = $s;

        return s.length;
      }

      return -1;
    })(d);
  }
}
