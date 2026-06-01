/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { linq, toArray } from "metano-runtime/system/linq";
import { Counter } from "./counter";
import { For, createSignal } from "solid-js";

export type CounterGroupProps = {};

export function CounterGroup(props: CounterGroupProps) {
  const countersCount = createSignal(1);
  const addCounter = () => countersCount[1](countersCount[0]() + 1);

  return (
    <div>
      <div class="counters">
        <For
          each={linq(
            Array.from({ length: countersCount[0]() }, (_, i) => 0 + i),
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
