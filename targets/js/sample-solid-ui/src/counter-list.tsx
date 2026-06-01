/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Counter } from "./counter";
import { For } from "solid-js";

export type CounterListProps = { seeds?: number[] };

export function CounterList(props: CounterListProps) {
  const props$seeds = props.seeds ?? [];

  return (
    <div class="counter-list">
      <For each={props$seeds}>{(item, index) => <Counter count={item} />}</For>
    </div>
  );
}
