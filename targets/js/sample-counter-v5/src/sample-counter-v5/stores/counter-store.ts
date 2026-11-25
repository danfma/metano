/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Counter } from "../models/counter.js";
import { createEffect, createSignal } from "solid-js";

export class CounterStore {
  private declare readonly _get: () => Counter;
  private declare readonly _set: ((value: Counter) => void) &
    ((updater: (arg: Counter) => Counter) => void);

  private constructor() {
    this._get = undefined!;
    this._set = undefined!;
    const [get, set] = createSignal(Counter.zero);
    this._get = get;
    this._set = set;
    createEffect(() => {
      console.log(`Counter has changed: ${this.state().count}`);
    });
  }

  state(): Counter {
    return this._get();
  }

  increment(): void {
    this._set((x: Counter) => x.increment());
  }

  decrement(): void {
    this._set((x: Counter) => x.decrement());
  }

  static create(): CounterStore {
    return new CounterStore();
  }
}
