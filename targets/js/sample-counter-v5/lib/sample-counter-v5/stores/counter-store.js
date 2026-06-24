/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Counter } from "#/sample-counter-v5/models/counter";
import { createEffect, createSignal } from "solid-js";
export class CounterStore {
    _get;
    _set;
    constructor() {
        const [get, set] = createSignal(Counter.zero);
        this._get = get;
        this._set = set;
        createEffect(() => {
            console.log(`Counter has changed: ${this.state().count}`);
        });
    }
    state() {
        return this._get();
    }
    increment() {
        this._set((x) => x.increment());
    }
    decrement() {
        this._set((x) => x.decrement());
    }
    static create() {
        return new CounterStore();
    }
}
