export class Counter {
  private _total: number = 0;

  constructor() { }

  add(a: number, b: number): number {
    return a + b;
  }

  hash(a: number): number {
    return a * 31 + 7;
  }

  accumulate(values: number[]): number {
    {
      for (const v of values) {
        this._total += v;
      }
    }

    return this._total;
  }

  nested(a: number): number {
    {
      let h = a * 17;

      {
        h += 1;
      }

      return h;
    }
  }

  doubler(): (arg: number) => number {
    {
      return (x: number) => x * 2;
    }
  }

  shiftValue(n: number): number {
    return n <<= 1;
  }

  text(a: number): string {
    return (a + 1).toString();
  }
}
