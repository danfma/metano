// `List.get` / `set` and the lowered `foreach` generated code emits (spec 011 FR-057), for the
// production-checks acceptance of FR-058: bundled with `__METANO_CHECKS__` defined `false` and
// without it.
import { List } from "./system/collections/list.js";
import { throwModified } from "./system/collections/throw-helpers.js";

export function run() {
  const xs = List.of([1, 2, 3]);
  xs.set(0, xs.get(0) + 10);
  let sum = 0;
  {
    const $c = typeof __METANO_CHECKS__ === "undefined" || __METANO_CHECKS__;
    const $l = xs;
    const $a = $l._items;
    const $n = $a.length;
    const $v = $c ? $l._version : 0;
    for (let $i = 0; ; $i++) {
      if ($c && ($l._version !== $v || $a.length !== $n)) throwModified();
      if ($i === $n) break;
      const x = $a[$i];
      sum += x;
    }
  }
  let outOfRange;
  try {
    outOfRange = xs.get(99);
  } catch (error) {
    outOfRange = error.name;
  }
  return [sum, outOfRange];
}
