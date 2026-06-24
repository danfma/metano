/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { createSignal } from "solid-js";

export type CounterProps = { count?: number };

export function Counter(props: CounterProps) {
  const props$count = props.count ?? 0;
  const [count, setCount] = createSignal(props$count);
  const decrement = () => setCount(count() - 1);
  const increment = () => setCount(count() + 1);

  return (
    <div class="counter">
      <button class="action" onClick={decrement}>
        -
      </button>
      <span class="display">{count()}</span>
      <button class="action" onClick={increment}>
        +
      </button>
    </div>
  );
}
