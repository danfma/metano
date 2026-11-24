import { Counter } from "./counter.js";
import { For } from "solid-js";

export type CounterListProps = { items?: number[] };

export function CounterList(props: CounterListProps) {
  const props$items = props.items ?? [];

  return <div><For each={props$items}>{(item, index) => <Counter count={item} />}</For></div>;
}
