export class Walks {
  private constructor() { }

  static converge(n: number): number {
    let sum = 0;
    for (let i = 0, j = n; i < j; i++, j--) {
      sum = (sum + (j - i)) | 0;
    }

    return sum;
  }

  static assigned(n: number): number {
    let k: number;
    let l: number;
    let total = 0;
    let steps = 0;
    for (k = 0, l = n; k < l; k = (k + 1) | 0, l = (l - 2) | 0, steps = (steps + 1) | 0) {
      total = (total + k) | 0;
    }

    return (total + steps) | 0;
  }

  static single(n: number): number {
    let total = 0;
    for (let i = 0; i < n; i++) {
      total = (total + i) | 0;
    }

    return total;
  }

  static forever(): number {
    for (; ; ) {
      return 1;
    }
  }
}
