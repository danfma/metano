import { Counter } from "./counter";
import { For } from "solid-js";

export type CounterListProps = { items?: number[] };

export function CounterList(props: CounterListProps) {
  const props$items = props.items ?? null;

  return <div><For each={props$items}>{(item, index) => <Counter count={item} />}</For></div>;
}
