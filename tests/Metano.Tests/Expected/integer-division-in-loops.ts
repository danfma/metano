import { intDiv } from "metano-runtime";

export class Halver {
  constructor() { }

  static steps(n: number): number {
    let steps = 0;
    for (let i = n; i > 0; i = intDiv(i, 2)) {
      steps = (steps + 1) | 0;
    }
    while (n > 0) {
      n = (n - (intDiv(n, 3) + 1)) | 0;
    }
    for (const x of [9, 18]) {
      steps = (steps + intDiv(x, 4)) | 0;
    }
    do {
      steps = (steps + intDiv(steps, 2)) | 0;
    } while (steps < 0);
    try {
      steps = (steps + intDiv(steps, 5)) | 0;
    } finally {
      n = intDiv(n, 2);
    }

    return (steps + n) | 0;
  }
}
