import { createEffect, createSignal } from "solid-js";

export type CounterProps = { count?: number };

export function Counter(props: CounterProps) {
  const props$count = props.count ?? 0;
  const count = createSignal(props$count);
  createEffect(() => console.log(count[0]()));
  const decrement = () => count[1](count[0]() - 1);
  const increment = () => count[1]((x: number) => x + 1);

  return <div><button class="dec" onClick={decrement}>-</button><span>{count[0]()}</span><button class="inc" onClick={increment}>+</button></div>;
}
