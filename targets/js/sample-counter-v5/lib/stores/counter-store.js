/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { createEffect, createSignal } from "solid-js";
import { Counter } from "#/models";
export class CounterStore {
    _counter;
    constructor() {
        this._counter = createSignal(Counter.zero);
        createEffect(() => {
            console.log(`Counter has changed: ${this.state().count}`);
        });
    }
    state() {
        return this._counter[0]();
    }
    increment() {
        this._counter[1]((x) => x.increment());
    }
    decrement() {
        this._counter[1]((x) => x.decrement());
    }
    static create() {
        return new CounterStore();
    }
}
