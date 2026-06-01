export type DivChildrenProps = { label?: string };

export function DivChildren(props: DivChildrenProps) {
  const props$label = props.label ?? null;

  return <div><button>-</button><span>{props$label}</span></div>;
}
