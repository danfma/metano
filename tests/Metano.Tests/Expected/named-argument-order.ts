import { stringSubstring } from "metano-runtime";
import { ArgumentException } from "metano-runtime/system/exceptions/argument-exception";
import { Chained } from "./chained.js";
import { Derived } from "./derived.js";
import { scale2 } from "./engine-block.js";
import { scale } from "./engine-extensions.js";
import { Engine } from "./engine.js";
import type { Holder } from "./holder.js";
import { blend } from "./mixer.js";
import type { Op } from "./op.js";
import { Pair } from "./pair.js";
import { PrimaryDerived } from "./primary-derived.js";
import { Rec } from "./rec.js";
import { Tone } from "./tone.js";
import { Trace } from "./trace.js";
import type { Tuner } from "./tuner.js";

export class SourceOrderCalls {
  private constructor() { }

  static instance(e: Engine): number {
    let $arg1, $arg0;

    return ($arg1 = Trace.next("p"), $arg0 = Trace.next("q"), e.single($arg0, $arg1));
  }

  static getters(e: Engine, h: Holder): number {
    let $arg1, $arg0;

    return ($arg1 = h.p, $arg0 = h.q, e.single($arg0, $arg1));
  }

  static static(): number {
    let $arg1, $arg0;

    return ($arg1 = Trace.next("p"), $arg0 = Trace.next("q"), Engine.mix($arg0, $arg1));
  }

  static new(h: Holder): number {
    let $arg1, $arg0;

    return ($arg1 = h.p, $arg0 = h.q, new Pair($arg0, $arg1)).total;
  }

  static targetTypedNew(): number {
    let $arg1, $arg0;
    const pair = ($arg1 = Trace.next("p"), $arg0 = Trace.next("q"), new Pair($arg0, $arg1));

    return pair.total;
  }

  static record(): number {
    let $arg1, $arg0;
    const rec = ($arg1 = Trace.next("p"), $arg0 = Trace.next("q"), new Rec($arg0, $arg1));

    return (Math.imul(rec.a, 10) + rec.b) | 0;
  }

  static baseInitializer(): number {
    return new Derived().total;
  }

  static primaryBase(): number {
    return new PrimaryDerived().total;
  }

  static thisInitializer(): number {
    return Chained.fromDefault().total;
  }

  static extension(e: Engine): number {
    let $arg0, $arg2, $arg1;

    return ($arg0 = e, $arg2 = Trace.next("p"), $arg1 = Trace.next("q"), scale($arg0, $arg1, $arg2));
  }

  static extensionReceiver(h: Holder): number {
    let $arg0, $arg2, $arg1;

    return ($arg0 = h.target, $arg2 = Trace.next("p"), $arg1 = Trace.next("q"), scale($arg0, $arg1, $arg2));
  }

  static module(): number {
    let $arg1, $arg0;

    return ($arg1 = Trace.next("p"), $arg0 = Trace.next("q"), blend($arg0, $arg1));
  }

  static makeOp(): Op {
    Trace.next("m");

    return (f: number, d: number) => (Math.imul(f, 10) + d) | 0;
  }

  static computedCallee(): number {
    let $recv, $arg1, $arg0;

    return ($recv = SourceOrderCalls.makeOp(), $arg1 = Trace.next("p"), $arg0 = Trace.next("q"), $recv($arg0, $arg1));
  }

  static thisReceiver(h: Holder): number {
    return h.onThis(new Engine());
  }

  static delegateProperty(h: Holder): number {
    let $recv, $arg1, $arg0;

    return ($recv = h.fn, $arg1 = Trace.next("p"), $arg0 = Trace.next("q"), $recv($arg0, $arg1));
  }

  static delegatePropertyInvoke(h: Holder): number {
    let $recv, $arg1, $arg0;

    return ($recv = h.fn, $arg1 = Trace.next("p"), $arg0 = Trace.next("q"), $recv($arg0, $arg1));
  }

  static delegateField(h: Holder): number {
    let $recv, $arg1, $arg0;

    return ($recv = h.field, $arg1 = Trace.next("p"), $arg0 = Trace.next("q"), $recv($arg0, $arg1));
  }

  static extensionBlock(h: Holder): number {
    let $arg0, $arg2, $arg1;

    return ($arg0 = h.target, $arg2 = Trace.next("p"), $arg1 = Trace.next("q"), scale2($arg0, $arg1, $arg2));
  }

  static emitOnReceiver(h: Holder): number {
    let $arg0, $arg2, $arg1;

    return ($arg0 = h.target, $arg2 = Trace.next("p"), $arg1 = Trace.next("q"), ($arg0.single($arg1, $arg2)));
  }

  static delegate(): number {
    let $arg1, $arg0;
    const op = (f: number, d: number) => (Math.imul(f, 10) + d) | 0;

    return ($arg1 = Trace.next("p"), $arg0 = Trace.next("q"), op($arg0, $arg1));
  }

  static delegateInvoke(): number {
    let $arg1, $arg0;
    const op = (f: number, d: number) => (Math.imul(f, 10) + d) | 0;

    return ($arg1 = Trace.next("p"), $arg0 = Trace.next("q"), op($arg0, $arg1));
  }

  static funcDelegate(): number {
    let $arg1, $arg0;
    const fn = (f: number, d: number) => (Math.imul(f, 10) + d) | 0;

    return ($arg1 = Trace.next("p"), $arg0 = Trace.next("q"), fn($arg0, $arg1));
  }

  static optionalSkipped(e: Engine): number {
    let $arg2, $arg0, $arg1;

    return ($arg2 = Trace.next("p"), $arg0 = Trace.next("q"), $arg1 = 7, e.opt($arg0, $arg1, $arg2));
  }

  static params(e: Engine): number {
    let $arg1, $arg0, $arg2;

    return ($arg1 = Trace.next("p"), $arg0 = Trace.next("q"), $arg2 = [1, 2], e.rest($arg0, $arg1, ...$arg2));
  }

  static receiver(h: Holder): number {
    let $recv, $arg1, $arg0;

    return ($recv = h.target, $arg1 = Trace.next("p"), $arg0 = Trace.next("q"), $recv.single($arg0, $arg1));
  }

  static nullConditional(e: Engine | null): number {
    let $arg1, $arg0;

    return (e == null ? null : ($arg1 = Trace.next("p"), $arg0 = Trace.next("q"), e.single($arg0, $arg1))) ?? -1;
  }

  static lambdaArgument(e: Engine): number {
    let $arg2, $arg1, $arg0;

    return ($arg2 = Trace.next("p"), $arg1 = Trace.next("q"), $arg0 = (x: number) => (x + 1) | 0, e.fold($arg0, $arg1, $arg2));
  }

  static plainObject(): number {
    let $arg1, $arg0;
    const dto = ($arg1 = Trace.next("p"), $arg0 = Trace.next("q"), { f: $arg0, d: $arg1 });

    return (Math.imul(dto.f, 10) + dto.d) | 0;
  }

  static emit(): number {
    let $arg1, $arg0;

    return ($arg1 = Trace.next("p"), $arg0 = Trace.next("q"), ($arg0 * 10 + $arg1));
  }

  static bclMapped(): number {
    let $arg1, $arg0;

    return ($arg1 = Trace.next("p") * 0, $arg0 = Trace.next("q"), Math.max($arg0, $arg1));
  }

  static nested(e: Engine): number {
    let $arg1, $arg0, $arg1_1, $arg0_1;

    return ($arg1_1 = ($arg1 = Trace.next("p"), $arg0 = Trace.next("q"), e.single($arg0, $arg1)), $arg0_1 = Trace.next("r"), e.single($arg0_1, $arg1_1));
  }

  static tag(tag: string): string {
    Trace.next(tag);

    return (tag ?? "") + (tag ?? "");
  }

  static paramsOmitted(e: Engine): number {
    let $arg1, $arg0;

    return ($arg1 = Trace.next("p"), $arg0 = Trace.next("q"), e.rest($arg0, $arg1));
  }

  static stringEnumArgument(t: Tuner): number {
    let $arg2, $arg1, $arg0;

    return ($arg2 = Trace.next("p"), $arg1 = Tone.High, $arg0 = Trace.next("q"), t.tuned($arg0, $arg1, $arg2));
  }

  static generic(t: Tuner): number {
    let $arg1, $arg0;

    return ($arg1 = Trace.next("p"), $arg0 = Trace.next("q"), t.pick($arg0, $arg1));
  }

  static nullArgument(t: Tuner): number {
    let $arg1, $arg0;

    return ($arg1 = SourceOrderCalls.tag("p"), $arg0 = (SourceOrderCalls.tag("q") ?? "") + "x", t.named($arg0, $arg1));
  }

  static bclInstance(): number {
    let $arg1, $arg0;

    return ($arg1 = Trace.next("p"), $arg0 = Trace.next("q"), stringSubstring("abcdefgh", $arg0, $arg1)).length;
  }

  static bclException(): number {
    let receiver$temp$0, receiver$temp$1;

    return (receiver$temp$0 = SourceOrderCalls.tag("p"), (receiver$temp$1 = SourceOrderCalls.tag("q"), new ArgumentException(receiver$temp$1, receiver$temp$0))).message.length;
  }
}

import { Engine } from "./engine.js";
import type { Op } from "./op.js";
import { Trace } from "./trace.js";

export class Holder {
  declare auto: number;
  declare field: Op;

  constructor() {
    this.auto = 3;
    this.field = (f: number, d: number) => (Math.imul(f, 10) + d) | 0;
  }

  get p(): number {
    return Trace.next("p");
  }

  get q(): number {
    return Trace.next("q");
  }

  get fn(): Op {
    Trace.next("g");

    return (f: number, d: number) => (Math.imul(f, 10) + d) | 0;
  }

  get target(): Engine {
    Trace.next("t");

    return new Engine();
  }

  onThis(e: Engine): number {
    let $arg1, $arg0;

    return ($arg1 = this.p, $arg0 = this.q, e.single($arg0, $arg1));
  }
}

import { Root } from "./root.js";
import { Trace } from "./trace.js";

export class Derived extends Root {
  constructor() {
    const __baseArg1 = Trace.next("p");
    const __baseArg0 = Trace.next("q");
    super(__baseArg0, __baseArg1);
  }
}

import { Root } from "./root.js";
import { Trace } from "./trace.js";

export class PrimaryDerived extends Root {
  constructor() {
    const __baseArg1 = Trace.next("p");
    const __baseArg0 = Trace.next("q");
    super(__baseArg0, __baseArg1);
  }
}

import { isInt32 } from "metano-runtime";
import { Trace } from "./trace.js";

export class Chained {
  protected static readonly byChainedFD = /* @__PURE__ */ Symbol("Chained(int f, int d)");
  protected static readonly byChainedDefault = /* @__PURE__ */ Symbol("Chained()");
  declare total: number;

  constructor(overload: typeof Chained.byChainedFD, f: number, d: number);
  constructor(overload: typeof Chained.byChainedDefault);
  constructor(f: number, d: number);
  constructor();
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Chained.byChainedFD) {
      const f = args[1] as number;
      const d = args[2] as number;
      this.total = 0;
      this.total = (Math.imul(f, 10) + d) | 0;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Chained.byChainedDefault) {
      const __chain0Arg0 = Trace.next("p");
      const __chain0Arg1 = Trace.next("q");

      {
        const f = __chain0Arg1;
        const d = __chain0Arg0;
        this.total = 0;
        this.total = (Math.imul(f, 10) + d) | 0;
      }

      return;
    }

    if (args.length === 2 && isInt32(args[0]) && isInt32(args[1])) {
      const f = args[0] as number;
      const d = args[1] as number;
      this.total = 0;
      this.total = (Math.imul(f, 10) + d) | 0;

      return;
    }

    if (args.length === 0) {
      const __chain0Arg0 = Trace.next("p");
      const __chain0Arg1 = Trace.next("q");

      {
        const f = __chain0Arg1;
        const d = __chain0Arg0;
        this.total = 0;
        this.total = (Math.imul(f, 10) + d) | 0;
      }

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromFD(f: number, d: number): Chained {
    return new Chained(Chained.byChainedFD, f, d);
  }

  static fromDefault(): Chained {
    return new Chained(Chained.byChainedDefault);
  }
}

import { List } from "metano-runtime/system/collections";
import type { Collector } from "./collector.js";
import { Trace } from "./trace.js";

export class ParamsCalls {
  private constructor() { }

  static arrayOmitted(e: Collector): number {
    let $arg0, $arg2, $arg1;

    return ($arg0 = Trace.next("x"), $arg2 = Trace.next("p"), $arg1 = Trace.next("q"), e.restA($arg0, $arg1, $arg2));
  }

  static arrayOmittedLastFirst(e: Collector): number {
    let $arg2, $arg0, $arg1;

    return ($arg2 = Trace.next("p"), $arg0 = Trace.next("x"), $arg1 = Trace.next("q"), e.restA($arg0, $arg1, $arg2));
  }

  static arrayGiven(e: Collector): number {
    let $arg2, $arg0, $arg1, $arg3;

    return ($arg2 = Trace.next("p"), $arg0 = Trace.next("x"), $arg1 = Trace.next("q"), $arg3 = [1, 2], e.restA($arg0, $arg1, $arg2, ...$arg3));
  }

  static listOmitted(e: Collector): number {
    let $arg0, $arg2, $arg1, $arg3;

    return ($arg0 = Trace.next("x"), $arg2 = Trace.next("p"), $arg1 = Trace.next("q"), $arg3 = List.of<number>([]), e.restL($arg0, $arg1, $arg2, $arg3));
  }

  static listPositionalFirst(e: Collector): number {
    let $arg0, $arg2, $arg1, $arg3;

    return ($arg0 = Trace.next("x"), $arg2 = Trace.next("p"), $arg1 = Trace.next("q"), $arg3 = List.of<number>([]), e.restL($arg0, $arg1, $arg2, $arg3));
  }

  static listGiven(e: Collector): number {
    let $arg0, $arg2, $arg1, $arg3;

    return ($arg0 = Trace.next("x"), $arg2 = Trace.next("p"), $arg1 = Trace.next("q"), $arg3 = List.of<number>([Trace.next("r")]), e.restL($arg0, $arg1, $arg2, $arg3));
  }

  static listPositional(e: Collector): number {
    return e.restL(Trace.next("x"), Trace.next("p"), Trace.next("q"), List.of<number>([]));
  }

  static enumerableOmitted(e: Collector): number {
    let $arg0, $arg2, $arg1, $arg3;

    return ($arg0 = Trace.next("x"), $arg2 = Trace.next("p"), $arg1 = Trace.next("q"), $arg3 = [], e.restE($arg0, $arg1, $arg2, $arg3));
  }

  static enumerableOmittedLastFirst(e: Collector): number {
    let $arg2, $arg0, $arg1, $arg3;

    return ($arg2 = Trace.next("p"), $arg0 = Trace.next("x"), $arg1 = Trace.next("q"), $arg3 = [], e.restE($arg0, $arg1, $arg2, $arg3));
  }
}

import { NativeBox } from "../native-box";
import { Trace } from "./trace.js";

export class AmbientCalls {
  private constructor() { }

  static delegateProperty(box: NativeBox): number {
    let $arg1, $arg0;

    return ($arg1 = Trace.next("p"), $arg0 = Trace.next("q"), box.calc($arg0, $arg1));
  }

  static funcProperty(box: NativeBox): number {
    let $arg1, $arg0;

    return ($arg1 = Trace.next("p"), $arg0 = Trace.next("q"), box.compute($arg0, $arg1));
  }

  static delegatePropertyInvoke(box: NativeBox): number {
    let $arg1, $arg0;

    return ($arg1 = Trace.next("p"), $arg0 = Trace.next("q"), box.calc($arg0, $arg1));
  }

  static make(): NativeBox {
    Trace.next("n");

    return new NativeBox();
  }

  static computedReceiver(): number {
    let $recv, $arg1, $arg0;

    return ($recv = AmbientCalls.make(), $arg1 = Trace.next("p"), $arg0 = Trace.next("q"), $recv.calc($arg0, $arg1));
  }
}
