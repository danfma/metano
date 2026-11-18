/** A node of a trie keyed by object identity; the factories memoize on the arguments' identity. */
export interface MemoNode<R> {
  children?: Map<object, MemoNode<R>>;
  value?: R;
}

/** The node for a key tuple under `root`, creating the path on first use. */
export function memoNodeFor<R>(root: MemoNode<R>, keys: ReadonlyArray<object>): MemoNode<R> {
  let node = root;

  for (const key of keys) {
    node.children ??= new Map();

    let child = node.children.get(key);

    if (child === undefined) {
      child = {};
      node.children.set(key, child);
    }

    node = child;
  }

  return node;
}
