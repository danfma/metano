export class Counter {
  private _count: number = 0;

  constructor() { }

  get count(): number {
    return this._count;
  }

  increment(by: number = 1): void {
    this._count = (this._count + by) | 0;
  }

  static startingAt(value: number): Counter {
    const counter = new Counter();
    counter.increment(value);

    return counter;
  }
}
