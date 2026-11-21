import { isType, type RuntimeType } from "metano-runtime";

export class Box<T> {
  declare readonly $typeArguments$Box: readonly [RuntimeType<T>];

  constructor($typeArgs?: readonly [RuntimeType<T>]) {
    Object.defineProperty(this, "$typeArguments$Box", { value: $typeArgs, enumerable: false });
  }

  matches(o: Object): boolean {
    return isType(o, this.$typeArguments$Box[0]);
  }
}

// ---
import { Int32Type } from "metano-runtime";
import { Box } from "./box";

export class IntBox extends Box<number> {
  constructor() {
    super([Int32Type]);
  }
}

// ---
import { List, listType } from "metano-runtime/system/collections";
import { UnknownType, isType, type RuntimeType } from "metano-runtime";
import { Box } from "./box";

export class Pair<U, V> extends Box<List<U>> {
  declare readonly $typeArguments$Pair: readonly [RuntimeType<U>, RuntimeType<V>];

  constructor($typeArgs?: readonly [RuntimeType<U>, RuntimeType<V>]) {
    super([listType(($typeArgs?.[0] ?? UnknownType))]);
    Object.defineProperty(this, "$typeArguments$Pair", { value: $typeArgs, enumerable: false });
  }

  matchesSecond(o: Object): boolean {
    return isType(o, this.$typeArguments$Pair[1]);
  }
}

// ---
import { Int32Type, StringType } from "metano-runtime";
import { IntBox } from "./int-box";
import { Pair } from "./pair";

export function check(a: Object, b: Object): boolean {
  const intBox = new IntBox();
  const pair = new Pair<string, number>([StringType, Int32Type]);

  return intBox.matches(a) && pair.matches(b) && pair.matchesSecond(a);
}
