/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { createSignal } from "solid-js";

export type CounterProps = { count?: number };

export function Counter(props: CounterProps) {
  const props$count = props.count ?? 0;
  const count = createSignal(props$count);
  const decrement = () => count[1](count[0]() - 1);
  const increment = () => count[1](count[0]() + 1);

  return (
    <div class="counter">
      <button class="action" onClick={decrement}>
        -
      </button>
      <span class="display">{count[0]()}</span>
      <button class="action" onClick={increment}>
        +
      </button>
    </div>
  );
}
