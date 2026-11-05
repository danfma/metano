import { isInt32 } from "metano-runtime";
import { Box } from "./box";
import type { IRank } from "./i-rank";

export class SortedBox<T extends IRank<T>> extends Box<T> {
  protected static readonly bySortedBoxValue: unique symbol = Symbol("SortedBox(T value)");

  protected static readonly bySortedBoxN: unique symbol = Symbol("SortedBox(int n)");

  constructor(overload: typeof SortedBox.bySortedBoxValue, value: T);
  constructor(overload: typeof SortedBox.bySortedBoxN, n: number);
  constructor(n: number);
  constructor(value: T);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === SortedBox.bySortedBoxValue) {
      const value = args[1] as T;
      super(value);

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === SortedBox.bySortedBoxN) {
      const n = args[1] as number;
      super(n);

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const n = args[0] as number;
      super(n);

      return;
    }

    if (args.length === 1 && typeof args[0] === "object") {
      const value = args[0] as T;
      super(value);

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromSortedBoxValue<T extends IRank<T>>(value: T): SortedBox<T> {
    return new SortedBox(SortedBox.bySortedBoxValue, value);
  }

  static fromSortedBoxN<T extends IRank<T>>(n: number): SortedBox<T> {
    return new SortedBox(SortedBox.bySortedBoxN, n);
  }
}

import { isInt32 } from "metano-runtime";
import { Box } from "./box";

export class Pair<K, V> extends Box<K> {
  protected static readonly byPairValue: unique symbol = Symbol("Pair(K value)");

  protected static readonly byPairN: unique symbol = Symbol("Pair(int n)");

  constructor(overload: typeof Pair.byPairValue, value: K);
  constructor(overload: typeof Pair.byPairN, n: number);
  constructor(n: number);
  constructor(value: K);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Pair.byPairValue) {
      const value = args[1] as K;
      super(value);

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Pair.byPairN) {
      const n = args[1] as number;
      super(n);

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const n = args[0] as number;
      super(n);

      return;
    }

    if (args.length === 1) {
      const value = args[0] as K;
      super(value);

      return;
    }

    throw new Error("No matching constructor");
  }

  static override fromValue<K, V>(value: K): Pair<K, V> {
    return new Pair(Pair.byPairValue, value);
  }

  static override fromN<K, V>(n: number): Pair<K, V> {
    return new Pair(Pair.byPairN, n);
  }
}
