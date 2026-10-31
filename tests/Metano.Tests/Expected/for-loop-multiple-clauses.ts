export class Walks {
  constructor() { }

  static converge(n: number): number {
    let sum = 0;
    for (let i = 0, j = n; i < j; i++, j--) {
      sum += j - i;
    }

    return sum;
  }

  static assigned(n: number): number {
    let k: number;
    let l: number;
    let total = 0;
    let steps = 0;
    for (k = 0, l = n; k < l; k++, l -= 2, steps++) {
      total += k;
    }

    return total + steps;
  }

  static single(n: number): number {
    let total = 0;
    for (let i = 0; i < n; i++) {
      total += i;
    }

    return total;
  }

  static forever(): number {
    for (; ; ) {
      return 1;
    }
  }
}
