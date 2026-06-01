export type CounterProps = { count?: number };

export function Counter(props: CounterProps) {
  const props$count = props.count ?? 0;

  return <span>{props$count}</span>;
}
