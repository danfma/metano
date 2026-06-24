import { CounterStore } from "#/sample-counter-v5/stores";
export function AppView() {
    const store = CounterStore.create();
    return (<div>
      <h1>Sample Counter with SolidJS</h1>

      <button type="button" onClick={() => store.decrement()}>
        Decrement
      </button>

      <div>{store.state().count} times clicked.</div>

      <button type="button" onClick={() => store.increment()}>
        Increment
      </button>
    </div>);
}
