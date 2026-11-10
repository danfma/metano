import { checkedInt32 } from "metano-runtime";

export class Counter {
  private _total: number = 0;

  constructor() { }

  add(a: number, b: number): number {
    return checkedInt32(a + b);
  }

  hash(a: number): number {
    return a * 31 + 7;
  }

  accumulate(values: number[]): number {
    {
      for (const v of values) {
        this._total = checkedInt32(this._total + v);
      }
    }

    return this._total;
  }

  nested(a: number): number {
    {
      let h = a * 17;

      {
        h = checkedInt32(h + 1);
      }

      return h;
    }
  }

  doubler(): (arg: number) => number {
    {
      return (x: number) => checkedInt32(x * 2);
    }
  }

  shiftValue(n: number): number {
    return n <<= 1;
  }

  text(a: number): string {
    return checkedInt32(a + 1).toString();
  }
}
