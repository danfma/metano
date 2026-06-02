import { linq, toArray } from "metano-runtime/system/linq";
import { Counter } from "./counter";
import { For, createSignal } from "solid-js";

export type CounterGroupProps = {};

export function CounterGroup(props: CounterGroupProps) {
  const [countersCount, setCountersCount] = createSignal(1);
  const addCounter = () => setCountersCount(countersCount() + 1);

  return (
    <div>
      <div class="counters">
        <For
          each={linq(
            Array.from({ length: countersCount() }, (_, i) => 0 + i),
            toArray(),
          )}
        >
          {() => <Counter />}
        </For>
      </div>
      <button class="add-action" onClick={addCounter}>
        Add counter
      </button>
    </div>
  );
}
