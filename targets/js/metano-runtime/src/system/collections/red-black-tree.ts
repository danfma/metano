/**
 * The red-black tree that `SortedSet` and `SortedDictionary` share (spec 011
 * FR-054). Internal: a pure ordered map, not re-exported by the
 * `system/collections` barrel. The comparer is injected; the tree throws
 * nothing of its own and keeps no version counter (the classes own both).
 * Every comparison runs before the tree changes shape, so a comparer that
 * throws leaves the tree as it was.
 *
 * The balancing follows the classic parent-pointer formulation (CLRS, as in
 * Java's `TreeMap`): O(log n) insert, delete and lookup, and in-order
 * navigation through `successor` / `predecessor` without a stack.
 */

/** A tree node; `key` / `value` move between nodes when a node with two children is deleted. */
export interface RedBlackNode<K, V> {
  key: K;
  value: V;
  left: RedBlackNode<K, V> | null;
  right: RedBlackNode<K, V> | null;
  parent: RedBlackNode<K, V> | null;
  red: boolean;
}

/** A three-way comparison: negative, zero (equal keys) or positive. */
export type CompareFunction<K> = (x: K, y: K) => number;

export class RedBlackTree<K, V> {
  readonly compare: CompareFunction<K>;
  root: RedBlackNode<K, V> | null;
  count: number;

  constructor(compare: CompareFunction<K>) {
    this.compare = compare;
    this.root = null;
    this.count = 0;
  }

  /** The node whose key compares equal to `key`, or `null`. */
  find(key: K): RedBlackNode<K, V> | null {
    let node = this.root;

    while (node !== null) {
      const order = this.compare(key, node.key);

      if (order === 0) {
        return node;
      }

      node = order < 0 ? node.left : node.right;
    }

    return null;
  }

  /**
   * Inserts `key` with `value` and returns the new node, or returns the
   * existing node of an equal key unchanged (the caller decides whether to
   * overwrite its value).
   */
  insert(key: K, value: V): { readonly node: RedBlackNode<K, V>; readonly inserted: boolean } {
    let parent: RedBlackNode<K, V> | null = null;
    let order = 0;
    let node = this.root;

    while (node !== null) {
      order = this.compare(key, node.key);

      if (order === 0) {
        return { node, inserted: false };
      }

      parent = node;
      node = order < 0 ? node.left : node.right;
    }

    const created: RedBlackNode<K, V> = { key, value, left: null, right: null, parent, red: true };

    if (parent === null) {
      this.root = created;
    } else if (order < 0) {
      parent.left = created;
    } else {
      parent.right = created;
    }

    this.count++;
    this.fixAfterInsertion(created);

    return { node: created, inserted: true };
  }

  /** Removes the node of `key`; `false` when no key compares equal. */
  delete(key: K): boolean {
    const node = this.find(key);

    if (node === null) {
      return false;
    }

    this.deleteNode(node);

    return true;
  }

  /** Removes `node`, which must belong to this tree. */
  deleteNode(node: RedBlackNode<K, V>): void {
    this.count--;

    let target = node;

    if (target.left !== null && target.right !== null) {
      const next = minimumOf(target.right);

      target.key = next.key;
      target.value = next.value;
      target = next;
    }

    const replacement = target.left ?? target.right;

    if (replacement !== null) {
      this.replace(target, replacement);
      target.left = target.right = target.parent = null;

      if (!target.red) {
        this.fixAfterDeletion(replacement);
      }

      return;
    }

    if (target.parent === null) {
      this.root = null;

      return;
    }

    if (!target.red) {
      this.fixAfterDeletion(target);
    }

    this.detach(target);
  }

  clear(): void {
    this.root = null;
    this.count = 0;
  }

  /** The smallest node, or `null` when empty. */
  first(): RedBlackNode<K, V> | null {
    return this.root === null ? null : minimumOf(this.root);
  }

  /** The largest node, or `null` when empty. */
  last(): RedBlackNode<K, V> | null {
    return this.root === null ? null : maximumOf(this.root);
  }

  /** The smallest node whose key is greater than or equal to `key`, or `null`. */
  ceiling(key: K): RedBlackNode<K, V> | null {
    let node = this.root;
    let best: RedBlackNode<K, V> | null = null;

    while (node !== null) {
      const order = this.compare(key, node.key);

      if (order === 0) {
        return node;
      }

      if (order < 0) {
        best = node;
        node = node.left;
      } else {
        node = node.right;
      }
    }

    return best;
  }

  /** The largest node whose key is less than or equal to `key`, or `null`. */
  floor(key: K): RedBlackNode<K, V> | null {
    let node = this.root;
    let best: RedBlackNode<K, V> | null = null;

    while (node !== null) {
      const order = this.compare(key, node.key);

      if (order === 0) {
        return node;
      }

      if (order > 0) {
        best = node;
        node = node.right;
      } else {
        node = node.left;
      }
    }

    return best;
  }

  private replace(target: RedBlackNode<K, V>, replacement: RedBlackNode<K, V>): void {
    const parent = target.parent;

    replacement.parent = parent;

    if (parent === null) {
      this.root = replacement;
    } else if (target === parent.left) {
      parent.left = replacement;
    } else {
      parent.right = replacement;
    }
  }

  private detach(target: RedBlackNode<K, V>): void {
    const parent = target.parent;

    if (parent !== null) {
      if (target === parent.left) {
        parent.left = null;
      } else {
        parent.right = null;
      }

      target.parent = null;
    }
  }

  private rotateLeft(node: RedBlackNode<K, V>): void {
    const pivot = node.right;

    if (pivot === null) {
      return;
    }

    node.right = pivot.left;

    if (pivot.left !== null) {
      pivot.left.parent = node;
    }

    this.replace(node, pivot);
    pivot.left = node;
    node.parent = pivot;
  }

  private rotateRight(node: RedBlackNode<K, V>): void {
    const pivot = node.left;

    if (pivot === null) {
      return;
    }

    node.left = pivot.right;

    if (pivot.right !== null) {
      pivot.right.parent = node;
    }

    this.replace(node, pivot);
    pivot.right = node;
    node.parent = pivot;
  }

  private fixAfterInsertion(inserted: RedBlackNode<K, V>): void {
    let node: RedBlackNode<K, V> | null = inserted;

    while (node !== null && node !== this.root && isRed(node.parent)) {
      const parent: RedBlackNode<K, V> | null = node.parent;
      const grandparent: RedBlackNode<K, V> | null = parentOf(parent);

      if (parent === leftOf(grandparent)) {
        const uncle = rightOf(grandparent);

        if (isRed(uncle)) {
          setBlack(parent);
          setBlack(uncle);
          setRed(grandparent);
          node = grandparent;
          continue;
        }

        if (node === rightOf(parent)) {
          node = parent;
          this.rotateLeftOf(node);
        }

        setBlack(parentOf(node));
        setRed(parentOf(parentOf(node)));
        this.rotateRightOf(parentOf(parentOf(node)));
        continue;
      }

      const uncle = leftOf(grandparent);

      if (isRed(uncle)) {
        setBlack(parent);
        setBlack(uncle);
        setRed(grandparent);
        node = grandparent;
        continue;
      }

      if (node === leftOf(parent)) {
        node = parent;
        this.rotateRightOf(node);
      }

      setBlack(parentOf(node));
      setRed(parentOf(parentOf(node)));
      this.rotateLeftOf(parentOf(parentOf(node)));
    }

    setBlack(this.root);
  }

  private fixAfterDeletion(start: RedBlackNode<K, V>): void {
    let node: RedBlackNode<K, V> | null = start;

    while (node !== null && node !== this.root && !isRed(node)) {
      if (node === leftOf(parentOf(node))) {
        let sibling = rightOf(parentOf(node));

        if (isRed(sibling)) {
          setBlack(sibling);
          setRed(parentOf(node));
          this.rotateLeftOf(parentOf(node));
          sibling = rightOf(parentOf(node));
        }

        if (!isRed(leftOf(sibling)) && !isRed(rightOf(sibling))) {
          setRed(sibling);
          node = parentOf(node);
          continue;
        }

        if (!isRed(rightOf(sibling))) {
          setBlack(leftOf(sibling));
          setRed(sibling);
          this.rotateRightOf(sibling);
          sibling = rightOf(parentOf(node));
        }

        setColor(sibling, isRed(parentOf(node)));
        setBlack(parentOf(node));
        setBlack(rightOf(sibling));
        this.rotateLeftOf(parentOf(node));
        node = this.root;
        continue;
      }

      let sibling = leftOf(parentOf(node));

      if (isRed(sibling)) {
        setBlack(sibling);
        setRed(parentOf(node));
        this.rotateRightOf(parentOf(node));
        sibling = leftOf(parentOf(node));
      }

      if (!isRed(rightOf(sibling)) && !isRed(leftOf(sibling))) {
        setRed(sibling);
        node = parentOf(node);
        continue;
      }

      if (!isRed(leftOf(sibling))) {
        setBlack(rightOf(sibling));
        setRed(sibling);
        this.rotateLeftOf(sibling);
        sibling = leftOf(parentOf(node));
      }

      setColor(sibling, isRed(parentOf(node)));
      setBlack(parentOf(node));
      setBlack(leftOf(sibling));
      this.rotateRightOf(parentOf(node));
      node = this.root;
    }

    setBlack(node);
  }

  private rotateLeftOf(node: RedBlackNode<K, V> | null): void {
    if (node !== null) {
      this.rotateLeft(node);
    }
  }

  private rotateRightOf(node: RedBlackNode<K, V> | null): void {
    if (node !== null) {
      this.rotateRight(node);
    }
  }
}

/** The in-order successor of `node`, or `null` after the largest node. */
export function successor<K, V>(node: RedBlackNode<K, V>): RedBlackNode<K, V> | null {
  if (node.right !== null) {
    return minimumOf(node.right);
  }

  let child = node;
  let parent = node.parent;

  while (parent !== null && child === parent.right) {
    child = parent;
    parent = parent.parent;
  }

  return parent;
}

/** The in-order predecessor of `node`, or `null` before the smallest node. */
export function predecessor<K, V>(node: RedBlackNode<K, V>): RedBlackNode<K, V> | null {
  if (node.left !== null) {
    return maximumOf(node.left);
  }

  let child = node;
  let parent = node.parent;

  while (parent !== null && child === parent.left) {
    child = parent;
    parent = parent.parent;
  }

  return parent;
}

function minimumOf<K, V>(start: RedBlackNode<K, V>): RedBlackNode<K, V> {
  let node = start;

  while (node.left !== null) {
    node = node.left;
  }

  return node;
}

function maximumOf<K, V>(start: RedBlackNode<K, V>): RedBlackNode<K, V> {
  let node = start;

  while (node.right !== null) {
    node = node.right;
  }

  return node;
}

// Null-safe accessors: a missing node is a black leaf, as in the textbook algorithm.

function isRed<K, V>(node: RedBlackNode<K, V> | null): boolean {
  return node?.red === true;
}

function parentOf<K, V>(node: RedBlackNode<K, V> | null): RedBlackNode<K, V> | null {
  return node === null ? null : node.parent;
}

function leftOf<K, V>(node: RedBlackNode<K, V> | null): RedBlackNode<K, V> | null {
  return node === null ? null : node.left;
}

function rightOf<K, V>(node: RedBlackNode<K, V> | null): RedBlackNode<K, V> | null {
  return node === null ? null : node.right;
}

function setColor<K, V>(node: RedBlackNode<K, V> | null, red: boolean): void {
  if (node !== null) {
    node.red = red;
  }
}

function setRed<K, V>(node: RedBlackNode<K, V> | null): void {
  setColor(node, true);
}

function setBlack<K, V>(node: RedBlackNode<K, V> | null): void {
  setColor(node, false);
}
