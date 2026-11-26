export class Cursor {
  private constructor() { }

  static tryNext(a: number[], i: number): readonly [found: boolean, v: number] {
    let v: number;
    v = i < a.length ? a[i]! : 0;

    return [i < a.length, v];
  }

  static sum(a: number[]): number {
    let i = 0;
    let total = 0;
    outer: while (true) {
      const [$found0, v] = Cursor.tryNext(a, ((i = (i + 1) | 0) - 1) | 0);

      if (!$found0) {
        break;
      }

      for (let k = 0; k < 2; k++) {
        if (v < 0) {
          continue outer;
        }

        if (v > 100) {
          break outer;
        }

        total = (total + v) | 0;
      }
    }

    return total;
  }

  static until(a: number[]): number {
    let i = 0;
    let total = 0;
    outer: while (true) {
      for (let k = 0; k < 2; k++) {
        if (a[i]! > 100) {
          break outer;
        }

        total = (total + a[i]!) | 0;
      }
      i = (i + 1) | 0;
      const [$found0, v] = Cursor.tryNext(a, i);

      if (!($found0 && v >= 0)) {
        break;
      }
    }

    return total;
  }
}
