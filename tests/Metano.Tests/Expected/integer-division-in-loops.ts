import { intDiv } from "metano-runtime";

export class Halver {
  constructor() { }

  static steps(n: number): number {
    let steps = 0;
    for (let i = n; i > 0; i = intDiv(i, 2)) {
      steps++;
    }
    while (n > 0) {
      n -= intDiv(n, 3) + 1;
    }
    for (const x of [9, 18]) {
      steps += intDiv(x, 4);
    }
    do {
      steps += intDiv(steps, 2);
    } while (steps < 0);
    try {
      steps += intDiv(steps, 5);
    } finally {
      n = intDiv(n, 2);
    }

    return steps + n;
  }
}
