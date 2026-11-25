export class Jumps {
  constructor() { }

  static scan(a: number[]): number {
    let n = 0;
    top: while (true) {
      while (n < a.length) {
        n = (n + 1) | 0;
        continue top;
      }
      break top;
    }

    return n;
  }

  static firstBlockedRow(grid: number[][]): number {
    let found = -1;
    rows: for (let row = 0; row < grid.length; row++) {
      cols: for (const cell of grid[row]!) {
        if (cell < 0) {
          continue rows;
        }

        if (cell === 0) {
          continue cols;
        }

        if (cell > 9) {
          found = row;
          break rows;
        }
      }
    }

    return found;
  }

  static countdown(n: number): number {
    let steps = 0;
    outer: do {
      for (let i = 0; i < 3; i++) {
        steps = (steps + 1) | 0;

        if (steps > 10) {
          break outer;
        }
      }
      n = (n - 1) | 0;
    } while (n > 0);

    return steps;
  }

  static dispatch(kind: number, steps: number[]): string {
    let log = "";

    dispatch: switch (kind) {
      case 1:
        for (const step of steps) {
          if (step < 0) {
            break dispatch;
          }

          log += step;
        }
        log += "!";
        break;
      default:
        log = "default";
        break;
    }

    return log;
  }
}
