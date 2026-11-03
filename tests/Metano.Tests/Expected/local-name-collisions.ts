export class Collisions {
  constructor() { }

  static paramVsLocal(total: number): number {
    const total$1 = 10;

    return total$1 + total;
  }

  static twoLocals(): number {
    const total = 1;
    const total$1 = 2;

    return total + total$1;
  }

  static lambdaOverLocal(): number {
    const factor = 3;
    const scale = (factor$1: number) => factor$1 * factor;

    return scale(2);
  }

  static forEachVsLocal(items: number[]): number {
    let total = 0;
    for (const total$1 of items) {
      total += total$1;
    }

    return total;
  }

  static catchVsLocal(): number {
    let ex = 0;
    try {
      ex = 1;
    } catch (ex$1) {
      return ex + ex$1.message.length;
    }

    return ex;
  }

  static designatorVsLocal(o: Object): number {
    const total = 1;
    let total$1: number;

    if (typeof o === "number" && (total$1 = o, true)) {
      return total + total$1;
    }

    return total;
  }

  static switchSectionLocals(n: number): number {
    let r = 0;

    switch (n) {
      case 1:
        const t = 1;
        r += t;
        break;
      case 2:
        const t$1 = 2;
        r += t$1;
        break;
    }

    return r;
  }
}
