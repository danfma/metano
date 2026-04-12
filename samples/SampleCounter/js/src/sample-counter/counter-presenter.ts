import { Counter } from "./counter";
import type { ICounterView } from "./i-counter-view";

export class CounterPresenter {
  private readonly _view: ICounterView;

  private _counter: Counter = Counter.zero;

  constructor(view: ICounterView) {
    this._view = view;
    this.initialize();
  }

  private initialize(): void {
    this._view.displayCounter(this._counter);
  }

  increment(): void {
    this._counter = this._counter.increment();
    this.displayCounter();
  }

  decrement(): void {
    this._counter = this._counter.decrement();
    this.displayCounter();
  }

  private displayCounter(): void {
    this._view.displayCounter(this._counter);
  }
}
