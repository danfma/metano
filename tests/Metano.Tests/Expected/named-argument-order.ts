import { Chained } from "./chained";
import { Derived } from "./derived";
import { Engine } from "./engine";
import { scale2 } from "./engine-block";
import { scale } from "./engine-extensions";
import type { Holder } from "./holder";
import { blend } from "./mixer";
import type { Op } from "./op";
import { Pair } from "./pair";
import { PrimaryDerived } from "./primary-derived";
import { Rec } from "./rec";
import { Tone } from "./tone";
import { Trace } from "./trace";
import type { Tuner } from "./tuner";

export class SourceOrderCalls {
  constructor() { }

  static instance(e: Engine): number {
    return (($arg1, $arg0) => e.single($arg0, $arg1))(Trace.next("p"), Trace.next("q"));
  }

  static getters(e: Engine, h: Holder): number {
    return (($arg1, $arg0) => e.single($arg0, $arg1))(h.p, h.q);
  }

  static static(): number {
    return (($arg1, $arg0) => Engine.mix($arg0, $arg1))(Trace.next("p"), Trace.next("q"));
  }

  static new(h: Holder): number {
    return (($arg1, $arg0) => new Pair($arg0, $arg1))(h.p, h.q).total;
  }

  static targetTypedNew(): number {
    const pair = (($arg1, $arg0) => new Pair($arg0, $arg1))(Trace.next("p"), Trace.next("q"));

    return pair.total;
  }

  static record(): number {
    const rec = (($arg1, $arg0) => new Rec($arg0, $arg1))(Trace.next("p"), Trace.next("q"));

    return rec.a * 10 + rec.b;
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
    return (($arg0, $arg2, $arg1) => scale($arg0, $arg1, $arg2))(e, Trace.next("p"), Trace.next("q"));
  }

  static extensionReceiver(h: Holder): number {
    return (($arg0, $arg2, $arg1) => scale($arg0, $arg1, $arg2))(h.target, Trace.next("p"), Trace.next("q"));
  }

  static module(): number {
    return (($arg1, $arg0) => blend($arg0, $arg1))(Trace.next("p"), Trace.next("q"));
  }

  static makeOp(): Op {
    Trace.next("m");

    return (f: number, d: number) => f * 10 + d;
  }

  static computedCallee(): number {
    return (($recv, $arg1, $arg0) => $recv($arg0, $arg1))(SourceOrderCalls.makeOp(), Trace.next("p"), Trace.next("q"));
  }

  static thisReceiver(h: Holder): number {
    return h.onThis(new Engine());
  }

  static delegateProperty(h: Holder): number {
    return (($recv, $arg1, $arg0) => $recv($arg0, $arg1))(h.fn, Trace.next("p"), Trace.next("q"));
  }

  static delegatePropertyInvoke(h: Holder): number {
    return (($recv, $arg1, $arg0) => $recv($arg0, $arg1))(h.fn, Trace.next("p"), Trace.next("q"));
  }

  static delegateField(h: Holder): number {
    return (($recv, $arg1, $arg0) => $recv($arg0, $arg1))(h.field, Trace.next("p"), Trace.next("q"));
  }

  static extensionBlock(h: Holder): number {
    return (($arg0, $arg2, $arg1) => scale2($arg0, $arg1, $arg2))(h.target, Trace.next("p"), Trace.next("q"));
  }

  static emitOnReceiver(h: Holder): number {
    return (($arg0, $arg2, $arg1) => ($arg0.single($arg1, $arg2)))(h.target, Trace.next("p"), Trace.next("q"));
  }

  static delegate(): number {
    const op = (f: number, d: number) => f * 10 + d;

    return (($arg1, $arg0) => op($arg0, $arg1))(Trace.next("p"), Trace.next("q"));
  }

  static delegateInvoke(): number {
    const op = (f: number, d: number) => f * 10 + d;

    return (($arg1, $arg0) => op($arg0, $arg1))(Trace.next("p"), Trace.next("q"));
  }

  static funcDelegate(): number {
    const fn = (f: number, d: number) => f * 10 + d;

    return (($arg1, $arg0) => fn($arg0, $arg1))(Trace.next("p"), Trace.next("q"));
  }

  static optionalSkipped(e: Engine): number {
    return (($arg2, $arg0, $arg1) => e.opt($arg0, $arg1, $arg2))(Trace.next("p"), Trace.next("q"), 7);
  }

  static params(e: Engine): number {
    return (($arg1, $arg0, $arg2) => e.rest($arg0, $arg1, ...$arg2))(Trace.next("p"), Trace.next("q"), [1, 2]);
  }

  static receiver(h: Holder): number {
    return (($recv, $arg1, $arg0) => $recv.single($arg0, $arg1))(h.target, Trace.next("p"), Trace.next("q"));
  }

  static nullConditional(e: Engine | null): number {
    return (e == null ? null : (($arg1, $arg0) => e.single($arg0, $arg1))(Trace.next("p"), Trace.next("q"))) ?? -1;
  }

  static lambdaArgument(e: Engine): number {
    return (($arg2, $arg1, $arg0) => e.fold($arg0, $arg1, $arg2))(Trace.next("p"), Trace.next("q"), (x: number) => x + 1);
  }

  static plainObject(): number {
    const dto = (($arg1, $arg0) => ({ f: $arg0, d: $arg1 }))(Trace.next("p"), Trace.next("q"));

    return dto.f * 10 + dto.d;
  }

  static emit(): number {
    return (($arg1, $arg0) => ($arg0 * 10 + $arg1))(Trace.next("p"), Trace.next("q"));
  }

  static bclMapped(): number {
    return (($arg1, $arg0) => Math.max($arg0, $arg1))(Trace.next("p") * 0, Trace.next("q"));
  }

  static nested(e: Engine): number {
    return (($arg1, $arg0) => e.single($arg0, $arg1))((($arg1, $arg0) => e.single($arg0, $arg1))(Trace.next("p"), Trace.next("q")), Trace.next("r"));
  }

  static tag(tag: string): string {
    Trace.next(tag);

    return tag + tag;
  }

  static paramsOmitted(e: Engine): number {
    return (($arg1, $arg0) => e.rest($arg0, $arg1))(Trace.next("p"), Trace.next("q"));
  }

  static stringEnumArgument(t: Tuner): number {
    return (($arg2, $arg1, $arg0) => t.tuned($arg0, $arg1, $arg2))(Trace.next("p"), Tone.High, Trace.next("q"));
  }

  static generic(t: Tuner): number {
    return (($arg1, $arg0) => t.pick($arg0, $arg1))(Trace.next("p"), Trace.next("q"));
  }

  static nullArgument(t: Tuner): number {
    return (($arg1, $arg0) => t.named($arg0, $arg1))(SourceOrderCalls.tag("p"), SourceOrderCalls.tag("q") + "x");
  }

  static bclInstance(): number {
    return (($arg1, $arg0) => "abcdefgh".substring($arg0, $arg1))(Trace.next("p"), Trace.next("q")).length;
  }

  static bclException(): number {
    return ((receiver$temp$0) => ((receiver$temp$1) => new Error(`${receiver$temp$1} (Parameter '${receiver$temp$0}')`))(SourceOrderCalls.tag("q")))(SourceOrderCalls.tag("p")).message.length;
  }
}

import { Engine } from "./engine";
import type { Op } from "./op";
import { Trace } from "./trace";

export class Holder {
  auto: number = 3;

  field: Op = (f: number, d: number) => f * 10 + d;

  constructor() { }

  get p(): number {
    return Trace.next("p");
  }

  get q(): number {
    return Trace.next("q");
  }

  get fn(): Op {
    Trace.next("g");

    return (f: number, d: number) => f * 10 + d;
  }

  get target(): Engine {
    Trace.next("t");

    return new Engine();
  }

  onThis(e: Engine): number {
    return (($arg1, $arg0) => e.single($arg0, $arg1))(this.p, this.q);
  }
}

import { Root } from "./root";
import { Trace } from "./trace";

export class Derived extends Root {
  constructor() {
    const __baseArg1 = Trace.next("p");
    const __baseArg0 = Trace.next("q");
    super(__baseArg0, __baseArg1);
  }
}

import { Root } from "./root";
import { Trace } from "./trace";

export class PrimaryDerived extends Root {
  constructor() {
    const __baseArg1 = Trace.next("p");
    const __baseArg0 = Trace.next("q");
    super(__baseArg0, __baseArg1);
  }
}

import { isInt32 } from "metano-runtime";
import { Trace } from "./trace";

export class Chained {
  protected static readonly byChainedFD: unique symbol = Symbol("Chained(int f, int d)");

  protected static readonly byChainedDefault: unique symbol = Symbol("Chained()");

  total: number = 0;

  constructor(overload: typeof Chained.byChainedFD, f: number, d: number);
  constructor(overload: typeof Chained.byChainedDefault);
  constructor(f: number, d: number);
  constructor();
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Chained.byChainedFD) {
      const f = args[1] as number;
      const d = args[2] as number;
      this.total = f * 10 + d;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Chained.byChainedDefault) {
      const __chain0Arg0 = Trace.next("p");
      const __chain0Arg1 = Trace.next("q");

      {
        const f = __chain0Arg1;
        const d = __chain0Arg0;
        this.total = f * 10 + d;
      }

      return;
    }

    if (args.length === 2 && isInt32(args[0]) && isInt32(args[1])) {
      const f = args[0] as number;
      const d = args[1] as number;
      this.total = f * 10 + d;

      return;
    }

    if (args.length === 0) {
      const __chain0Arg0 = Trace.next("p");
      const __chain0Arg1 = Trace.next("q");

      {
        const f = __chain0Arg1;
        const d = __chain0Arg0;
        this.total = f * 10 + d;
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

import type { Collector } from "./collector";
import { Trace } from "./trace";

export class ParamsCalls {
  constructor() { }

  static arrayOmitted(e: Collector): number {
    return (($arg0, $arg2, $arg1) => e.restA($arg0, $arg1, $arg2))(Trace.next("x"), Trace.next("p"), Trace.next("q"));
  }

  static arrayOmittedLastFirst(e: Collector): number {
    return (($arg2, $arg0, $arg1) => e.restA($arg0, $arg1, $arg2))(Trace.next("p"), Trace.next("x"), Trace.next("q"));
  }

  static arrayGiven(e: Collector): number {
    return (($arg2, $arg0, $arg1, $arg3) => e.restA($arg0, $arg1, $arg2, ...$arg3))(Trace.next("p"), Trace.next("x"), Trace.next("q"), [1, 2]);
  }

  static listOmitted(e: Collector): number {
    return (($arg0, $arg2, $arg1) => e.restL($arg0, $arg1, $arg2))(Trace.next("x"), Trace.next("p"), Trace.next("q"));
  }

  static listPositionalFirst(e: Collector): number {
    return (($arg0, $arg2, $arg1) => e.restL($arg0, $arg1, $arg2))(Trace.next("x"), Trace.next("p"), Trace.next("q"));
  }

  static listGiven(e: Collector): number {
    return (($arg0, $arg2, $arg1, $arg3) => e.restL($arg0, $arg1, $arg2, ...$arg3))(Trace.next("x"), Trace.next("p"), Trace.next("q"), [Trace.next("r")]);
  }

  static listPositional(e: Collector): number {
    return e.restL(Trace.next("x"), Trace.next("p"), Trace.next("q"));
  }

  static enumerableOmitted(e: Collector): number {
    return (($arg0, $arg2, $arg1) => e.restE($arg0, $arg1, $arg2))(Trace.next("x"), Trace.next("p"), Trace.next("q"));
  }

  static enumerableOmittedLastFirst(e: Collector): number {
    return (($arg2, $arg0, $arg1) => e.restE($arg0, $arg1, $arg2))(Trace.next("p"), Trace.next("x"), Trace.next("q"));
  }
}

import { NativeBox } from "../native-box";
import { Trace } from "./trace";

export class AmbientCalls {
  constructor() { }

  static delegateProperty(box: NativeBox): number {
    return (($arg1, $arg0) => box.calc($arg0, $arg1))(Trace.next("p"), Trace.next("q"));
  }

  static funcProperty(box: NativeBox): number {
    return (($arg1, $arg0) => box.compute($arg0, $arg1))(Trace.next("p"), Trace.next("q"));
  }

  static delegatePropertyInvoke(box: NativeBox): number {
    return (($arg1, $arg0) => box.calc($arg0, $arg1))(Trace.next("p"), Trace.next("q"));
  }

  static make(): NativeBox {
    Trace.next("n");

    return new NativeBox();
  }

  static computedReceiver(): number {
    return (($recv, $arg1, $arg0) => $recv.calc($arg0, $arg1))(AmbientCalls.make(), Trace.next("p"), Trace.next("q"));
  }
}
