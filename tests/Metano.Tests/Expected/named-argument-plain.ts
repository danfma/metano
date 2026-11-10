import { boxed } from "./boxes";
import type { Engine } from "./engine";
import type { Holder } from "./holder";
import type { Op } from "./op";
import { Trace } from "./trace";

export class PlainCalls {
  constructor() { }

  static expandedParams(e: Engine): number {
    return e.rest(Trace.next("p"), Trace.next("q"), 7, 8);
  }

  static nonTrailingNamed(e: Engine): number {
    return e.single(Trace.next("p"), Trace.next("q"));
  }

  static inOrder(e: Engine): number {
    return e.single(Trace.next("p"), Trace.next("q"));
  }

  static oneSensitive(e: Engine): number {
    return e.single(4, Trace.next("p"));
  }

  static autoReads(e: Engine, h: Holder, x: number): number {
    return e.single(x, h.auto);
  }

  static objectArgs(): number {
    return boxed({ d: Trace.next("p"), f: Trace.next("q") });
  }

  static optionalDelegate(op: Op | null): number {
    return op?.(Trace.next("q"), Trace.next("p")) ?? -1;
  }

  static paramsOmittedInOrder(e: Engine): number {
    return e.rest(Trace.next("p"), Trace.next("q"));
  }

  static constantFirst(e: Engine): number {
    return e.single(Trace.next("p"), 4);
  }
}
