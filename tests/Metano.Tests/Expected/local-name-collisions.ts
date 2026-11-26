import { isInt32, toException } from "metano-runtime";

export class Collisions {
  static paramVsLocal(total: number): number {
    const total$1 = 10;

    return (total$1 + total) | 0;
  }

  static twoLocals(): number {
    const total = 1;
    const total$1 = 2;

    return (total + total$1) | 0;
  }

  static lambdaOverLocal(): number {
    const factor = 3;
    const scale = (factor$1: number) => Math.imul(factor$1, factor);

    return scale(2);
  }

  static forEachVsLocal(items: number[]): number {
    let total = 0;
    for (const total$1 of items) {
      total = (total + total$1) | 0;
    }

    return total;
  }

  static catchVsLocal(): number {
    let ex = 0;
    try {
      ex = 1;
    } catch (e$) {
      const ex$1 = toException(e$);

      return (ex + ex$1.message.length) | 0;
    }

    return ex;
  }

  static designatorVsLocal(o: Object): number {
    const total = 1;
    let total$1: number;

    if (isInt32(o) && (total$1 = o, true)) {
      return (total + total$1) | 0;
    }

    return total;
  }

  static switchSectionLocals(n: number): number {
    let r = 0;

    switch (n) {
      case 1:
        const t = 1;
        r = (r + t) | 0;
        break;
      case 2:
        const t$1 = 2;
        r = (r + t$1) | 0;
        break;
    }

    return r;
  }
}
