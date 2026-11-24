import { Counter } from "./counter.js";

export type CounterGroupProps = {};

export function CounterGroup(props: CounterGroupProps) {
  return <div><Counter count={3} /><Counter /></div>;
}
