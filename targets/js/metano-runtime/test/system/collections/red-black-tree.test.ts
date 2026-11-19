import { describe, expect, test } from "bun:test";
import {
  predecessor,
  RedBlackTree,
  type RedBlackNode,
  successor,
} from "#/system/collections/red-black-tree.ts";
import { createSeededRandom, modelSeeds } from "./model/seeded-random.ts";

const byNumber = (x: number, y: number): number => x - y;

/** Asserts the red-black and binary-search invariants and returns the keys in order. */
function checkInvariants(tree: RedBlackTree<number, string>): number[] {
  const keys: number[] = [];

  const blackHeight = (node: RedBlackNode<number, string> | null): number => {
    if (node === null) {
      return 1;
    }

    for (const child of [node.left, node.right]) {
      if (child !== null) {
        expect(child.parent).toBe(node);
        expect(node.red && child.red).toBe(false);
      }
    }

    const left = blackHeight(node.left);

    keys.push(node.key);

    const right = blackHeight(node.right);

    expect(left).toBe(right);

    return left + (node.red ? 0 : 1);
  };

  if (tree.root !== null) {
    expect(tree.root.parent).toBeNull();
    expect(tree.root.red).toBe(false);
  }

  blackHeight(tree.root);
  expect(tree.count).toBe(keys.length);

  for (let i = 1; i < keys.length; i++) {
    expect((keys[i - 1] as number) < (keys[i] as number)).toBe(true);
  }

  return keys;
}

function forwardKeys(tree: RedBlackTree<number, string>): number[] {
  const keys: number[] = [];

  for (let node = tree.first(); node !== null; node = successor(node)) {
    keys.push(node.key);
  }

  return keys;
}

function backwardKeys(tree: RedBlackTree<number, string>): number[] {
  const keys: number[] = [];

  for (let node = tree.last(); node !== null; node = predecessor(node)) {
    keys.push(node.key);
  }

  return keys;
}

describe("RedBlackTree", () => {
  test("an empty tree has no nodes", () => {
    const tree = new RedBlackTree<number, string>(byNumber);

    expect(tree.count).toBe(0);
    expect(tree.first()).toBeNull();
    expect(tree.last()).toBeNull();
    expect(tree.find(1)).toBeNull();
    expect(tree.ceiling(1)).toBeNull();
    expect(tree.floor(1)).toBeNull();
    expect(tree.delete(1)).toBe(false);
  });

  test("insert returns the existing node of an equal key unchanged", () => {
    const tree = new RedBlackTree<number, string>(byNumber);
    const first = tree.insert(5, "a");
    const second = tree.insert(5, "b");

    expect(first.inserted).toBe(true);
    expect(second.inserted).toBe(false);
    expect(second.node).toBe(first.node);
    expect(second.node.value).toBe("a");
    expect(tree.count).toBe(1);
  });

  test("the comparer alone decides equality", () => {
    const byMagnitude = (x: number, y: number): number => Math.abs(x) - Math.abs(y);
    const tree = new RedBlackTree<number, string>(byMagnitude);

    tree.insert(-3, "minus");

    expect(tree.insert(3, "plus").inserted).toBe(false);
    expect(tree.find(3)?.key).toBe(-3);
  });

  test("ceiling and floor find the nearest keys", () => {
    const tree = new RedBlackTree<number, string>(byNumber);

    for (const key of [10, 20, 30, 40]) {
      tree.insert(key, String(key));
    }

    expect(tree.ceiling(20)?.key).toBe(20);
    expect(tree.ceiling(21)?.key).toBe(30);
    expect(tree.ceiling(41)).toBeNull();
    expect(tree.floor(20)?.key).toBe(20);
    expect(tree.floor(19)?.key).toBe(10);
    expect(tree.floor(9)).toBeNull();
  });

  test("a comparer that throws leaves the tree unchanged", () => {
    const tree = new RedBlackTree<number, string>((x, y) => {
      if (x === 99 || y === 99) {
        throw new Error("cannot compare");
      }

      return x - y;
    });

    for (const key of [1, 2, 3, 4, 5]) {
      tree.insert(key, String(key));
    }

    expect(() => tree.insert(99, "x")).toThrow("cannot compare");
    expect(() => tree.delete(99)).toThrow("cannot compare");
    expect(checkInvariants(tree)).toEqual([1, 2, 3, 4, 5]);
  });

  test("deleting a node with two children keeps every other key's value", () => {
    const tree = new RedBlackTree<number, string>(byNumber);

    for (const key of [4, 2, 6, 1, 3, 5, 7]) {
      tree.insert(key, `v${key}`);
    }

    expect(tree.delete(4)).toBe(true);
    expect(checkInvariants(tree)).toEqual([1, 2, 3, 5, 6, 7]);

    for (const key of [1, 2, 3, 5, 6, 7]) {
      expect(tree.find(key)?.value).toBe(`v${key}`);
    }
  });

  test("clear empties the tree", () => {
    const tree = new RedBlackTree<number, string>(byNumber);

    tree.insert(1, "a");
    tree.insert(2, "b");
    tree.clear();

    expect(tree.count).toBe(0);
    expect(tree.root).toBeNull();
  });

  test("ascending and descending runs stay balanced", () => {
    const tree = new RedBlackTree<number, string>(byNumber);
    const count = 4096;

    for (let key = 0; key < count; key++) {
      tree.insert(key, String(key));
    }

    checkInvariants(tree);

    // A red-black tree's height is at most 2·log2(n + 1).
    const height = (node: RedBlackNode<number, string> | null): number =>
      node === null ? 0 : 1 + Math.max(height(node.left), height(node.right));

    expect(height(tree.root)).toBeLessThanOrEqual(2 * Math.log2(count + 1));

    for (let key = count - 1; key >= 0; key -= 2) {
      tree.delete(key);
    }

    expect(checkInvariants(tree).length).toBe(count / 2);
  });

  for (const seed of modelSeeds()) {
    test(`random inserts and deletes match a sorted-array model (seed ${seed})`, () => {
      const random = createSeededRandom(seed);
      const tree = new RedBlackTree<number, string>(byNumber);
      const model = new Map<number, string>();

      for (let step = 0; step < 4000; step++) {
        const key = random.below(200);

        if (random.next() < 0.55) {
          const value = `s${step}`;
          const result = tree.insert(key, value);

          expect(result.inserted).toBe(!model.has(key));

          if (result.inserted) {
            model.set(key, value);
          }
        } else {
          expect(tree.delete(key)).toBe(model.delete(key));
        }

        if (step % 50 !== 0) {
          continue;
        }

        const expected = [...model.keys()].sort(byNumber);

        expect(checkInvariants(tree)).toEqual(expected);
        expect(forwardKeys(tree)).toEqual(expected);
        expect(backwardKeys(tree)).toEqual([...expected].reverse());

        for (const [modelKey, modelValue] of model) {
          expect(tree.find(modelKey)?.value).toBe(modelValue);
        }

        const probe = random.below(210) - 5;
        const ceiling = expected.find((k) => k >= probe) ?? null;
        const floor = [...expected].reverse().find((k) => k <= probe) ?? null;

        expect(tree.ceiling(probe)?.key ?? null).toBe(ceiling);
        expect(tree.floor(probe)?.key ?? null).toBe(floor);
      }
    });
  }
});
