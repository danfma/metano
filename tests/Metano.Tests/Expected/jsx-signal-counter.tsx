import { createEffect, createSignal } from "solid-js";

export type CounterProps = { count?: number };

export function Counter(props: CounterProps) {
  const props$count = props.count ?? 0;
  const [count, setCount] = createSignal(props$count);
  createEffect(() => console.log(count()));
  const decrement = () => setCount(count() - 1);
  const increment = () => setCount((x: number) => x + 1);

  return <div><button class="dec" onClick={decrement}>-</button><span>{count()}</span><button class="inc" onClick={increment}>+</button></div>;
}
