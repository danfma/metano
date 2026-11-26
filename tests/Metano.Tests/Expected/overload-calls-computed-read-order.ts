import { scaleFD } from "./engine-extensions.js";
import { Engine } from "./engine.js";
import { Holder } from "./holder.js";
import type { IEngine } from "./i-engine.js";
import type { IReading } from "./i-reading.js";
import { blendFD } from "./mixer.js";
import type { Plain } from "./plain.js";
import { Trace } from "./trace.js";

export class SourceOrderCalls {
  private constructor() { }

  static getters(e: Engine, h: Holder): number {
    let $arg1, $arg0;

    return ($arg1 = h.p, $arg0 = h.q, e.applyFD($arg0, $arg1));
  }

  static getterAndAuto(e: Engine, h: Holder): number {
    let $arg1, $arg0;

    return ($arg1 = h.auto, $arg0 = h.bump, e.applyFD($arg0, $arg1));
  }

  static getterAndField(e: Engine, h: Holder): number {
    let $arg1, $arg0;

    return ($arg1 = h.field, $arg0 = h.bumpField, e.applyFD($arg0, $arg1));
  }

  static getterAndLocal(e: Engine, h: Holder, x: number): number {
    let $arg1, $arg0;

    return ($arg1 = x, $arg0 = h.p, e.applyFD($arg0, $arg1));
  }

  static staticGetter(e: Engine, h: Holder): number {
    let $arg1, $arg0;

    return ($arg1 = Holder.s, $arg0 = h.p, e.applyFD($arg0, $arg1));
  }

  static construction(h: Holder): number {
    let $arg1, $arg0;

    return ($arg1 = h.p, $arg0 = h.q, Engine.fromFD($arg0, $arg1)).total;
  }

  static staticCall(h: Holder): number {
    let $arg1, $arg0;

    return ($arg1 = h.p, $arg0 = h.q, Engine.mixFD($arg0, $arg1));
  }

  static conditional(e: Engine | null, h: Holder): number {
    let $arg1, $arg0;

    return (e == null ? null : ($arg1 = h.p, $arg0 = h.q, e.applyFD($arg0, $arg1))) ?? -1;
  }

  static indexer(e: Engine, xs: number[]): number {
    let $arg1, $arg0;

    return ($arg1 = xs[Trace.next("i")]!, $arg0 = xs[Trace.next("j")]!, e.applyFD($arg0, $arg1));
  }

  static getterReceiver(h: Holder): number {
    let $recv, $arg1, $arg0;

    return ($recv = h.target, $arg1 = h.p, $arg0 = h.q, $recv.applyFD($arg0, $arg1));
  }

  static interfaceGetter(e: Engine, r: IReading): number {
    let $arg1, $arg0;

    return ($arg1 = r.value, $arg0 = (r.value + 5) | 0, e.applyFD($arg0, $arg1));
  }

  static overridableAuto(e: Engine, p: Plain): number {
    let $arg1, $arg0;

    return ($arg1 = p.slot, $arg0 = p.slot, e.applyFD($arg0, $arg1));
  }

  static otherAssembly(e: Engine, h: Holder, name: string): number {
    let $arg1, $arg0;

    return ($arg1 = name.length, $arg0 = h.auto, e.applyFD($arg0, $arg1));
  }

  static extension(e: Engine, h: Holder): number {
    let $arg0, $arg2, $arg1;

    return ($arg0 = e, $arg2 = h.p, $arg1 = h.q, scaleFD($arg0, $arg1, $arg2));
  }

  static moduleFunction(h: Holder): number {
    let $arg1, $arg0;

    return ($arg1 = h.p, $arg0 = h.q, blendFD($arg0, $arg1));
  }

  static interfaceReceiver(e: IEngine, h: Holder): number {
    let $arg1, $arg0;

    return ($arg1 = h.p, $arg0 = h.q, e.applyFD($arg0, $arg1));
  }

  static throwing(e: Engine, h: Holder): number {
    let $arg1, $arg0;

    return ($arg1 = h.throwA, $arg0 = h.throwB, e.applyFD($arg0, $arg1));
  }

  static formattedOperands(e: Engine, h: Holder): number {
    let $arg1, $arg0;

    return ($arg1 = "x" + (h.first ?? ""), $arg0 = "y" + (h.second ?? ""), e.joinFD($arg0, $arg1));
  }
}

import { InvalidOperationException } from "metano-runtime/system/exceptions/invalid-operation-exception";
import { Engine } from "./engine.js";
import { Label } from "./label.js";
import { Trace } from "./trace.js";

export class Holder {
  declare field: number;
  declare auto: number;
  static shared: number = 0;
  declare first: Label;
  declare second: Label;

  constructor() {
    this.field = 0;
    this.auto = 0;

    {
      const receiver$temp$0 = new Label();
      receiver$temp$0.text = "a";
      this.first = receiver$temp$0;
    }

    {
      const receiver$temp$0 = new Label();
      receiver$temp$0.text = "b";
      this.second = receiver$temp$0;
    }
  }

  get p(): number {
    return Trace.next("p");
  }

  get q(): number {
    return Trace.next("q");
  }

  static get s(): number {
    return Trace.next("s");
  }

  get bump(): number {
    this.auto = (this.auto + 1) | 0;

    return this.auto;
  }

  get bumpField(): number {
    this.field = (this.field + 1) | 0;

    return this.field;
  }

  get target(): Engine {
    Trace.next("t");

    return Engine.fromDefault();
  }

  get throwA(): number {
    Trace.next("A");
    throw new InvalidOperationException("A");
  }

  get throwB(): number {
    Trace.next("B");
    throw new InvalidOperationException("B");
  }

  onThis(e: Engine): number {
    let $arg1, $arg0;

    return ($arg1 = this.p, $arg0 = this.q, e.applyFD($arg0, $arg1));
  }
}

import { Engine } from "./engine.js";
import type { Holder } from "./holder.js";

export class Derived extends Engine {
  constructor(h: Holder) {
    const __baseArg1 = h.p;
    const __baseArg0 = h.q;
    super(Engine.byEngineFD, __baseArg0, __baseArg1);
  }
}

import { Engine } from "./engine.js";
import type { Holder } from "./holder.js";

export class Tuned extends Engine {
  constructor(h: Holder) {
    const __baseArg1 = h.p;
    const __baseArg0 = h.q;
    super(Engine.byEngineFD, __baseArg0, __baseArg1);
  }
}
