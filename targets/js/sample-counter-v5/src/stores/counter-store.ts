/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { createEffect, createSignal, type Signal as ISignal } from "solid-js";
import { Counter } from "#/models";

export class CounterStore {
  private readonly _counter: ISignal<Counter>;

  constructor() {
    this._counter = createSignal(Counter.zero);
    createEffect(() => {
      console.log(`Counter has changed: ${this.state().count}`);
    });
  }

  state(): Counter {
    return this._counter[0]();
  }

  increment(): void {
    this._counter[1]((x: Counter) => x.increment());
  }

  decrement(): void {
    this._counter[1]((x: Counter) => x.decrement());
  }

  static create(): CounterStore {
    return new CounterStore();
  }
}
