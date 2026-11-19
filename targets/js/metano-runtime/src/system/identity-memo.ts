/** A node of a trie keyed by object identity; the factories memoize on the arguments' identity. */
export interface MemoNode<R> {
  children?: Map<object, MemoNode<R>>;
  value?: R;
}

/** The node for a key tuple under `root`, creating the path on first use. */
export function memoNodeFor<R>(root: MemoNode<R>, keys: ReadonlyArray<object>): MemoNode<R> {
  return keys.reduce((node, key) => childOf(node, key), root);
}

function childOf<R>(node: MemoNode<R>, key: object): MemoNode<R> {
  const children = (node.children ??= new Map());
  const known = children.get(key);

  if (known !== undefined) {
    return known;
  }

  const created: MemoNode<R> = {};

  children.set(key, created);

  return created;
}
