export class Countdown implements Iterator<number> {
  private _value: number = 4;

  constructor() { }

  get current(): number {
    return this._value;
  }

  moveNext(): boolean {
    return --this._value > 0;
  }

  reset(): void {
    this._value = 4;
  }

  dispose(): void {
  }

  next(): IteratorResult<number> {
    return this.moveNext() ? { value: this.current, done: false } : { value: undefined, done: true };
  }
}
