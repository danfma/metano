import { Engine } from "./engine";
import { Holder } from "./holder";
import type { IReading } from "./i-reading";
import type { Plain } from "./plain";
import type { Point } from "./point";
import { Trace } from "./trace";

export class Calls {
  constructor() { }

  static getters(e: Engine, h: Holder): number {
    return (($arg1, $arg0) => e.applyFD($arg0, $arg1))(h.p, h.q);
  }

  static getterAndAuto(e: Engine, h: Holder): number {
    return (($arg1, $arg0) => e.applyFD($arg0, $arg1))(h.auto, h.bump);
  }

  static getterAndField(e: Engine, h: Holder): number {
    return (($arg1, $arg0) => e.applyFD($arg0, $arg1))(h.field, h.bumpField);
  }

  static getterAndLocal(e: Engine, h: Holder, x: number): number {
    return (($arg1, $arg0) => e.applyFD($arg0, $arg1))(x, h.p);
  }

  static staticGetter(e: Engine, h: Holder): number {
    return (($arg1, $arg0) => e.applyFD($arg0, $arg1))(Holder.s, h.p);
  }

  static construction(h: Holder): number {
    return (($arg1, $arg0) => Engine.fromFD($arg0, $arg1))(h.p, h.q).total;
  }

  static staticCall(h: Holder): number {
    return (($arg1, $arg0) => Engine.mixFD($arg0, $arg1))(h.p, h.q);
  }

  static conditional(e: Engine | null, h: Holder): number {
    return (e == null ? null : (($arg1, $arg0) => e.applyFD($arg0, $arg1))(h.p, h.q)) ?? -1;
  }

  static indexer(e: Engine, xs: number[]): number {
    return (($arg1, $arg0) => e.applyFD($arg0, $arg1))(xs[Trace.next("i")], xs[Trace.next("j")]);
  }

  static getterReceiver(h: Holder): number {
    return (($recv, $arg1, $arg0) => $recv.applyFD($arg0, $arg1))(h.target, h.p, h.q);
  }

  static interfaceGetter(e: Engine, r: IReading): number {
    return (($arg1, $arg0) => e.applyFD($arg0, $arg1))(r.value, r.value + 5);
  }

  static overridableAuto(e: Engine, p: Plain): number {
    return (($arg1, $arg0) => e.applyFD($arg0, $arg1))(p.slot, p.slot);
  }

  static otherAssembly(e: Engine, h: Holder, name: string): number {
    return (($arg1, $arg0) => e.applyFD($arg0, $arg1))(name.length, h.auto);
  }

  static insideOneArgument(e: Engine, h: Holder): number {
    return e.applyFD(7, h.p - h.q);
  }

  static getterAndConstant(e: Engine, h: Holder): number {
    return e.applyFD(7, h.p);
  }

  static autoAndField(e: Engine, h: Holder): number {
    return e.applyFD(h.field, h.auto);
  }

  static staticAutos(e: Engine): number {
    return e.applyFD(Holder.shared, Holder.shared);
  }

  static positionalRecord(e: Engine, p: Point): number {
    return e.applyFD(p.y, p.x);
  }

  static inParameterOrder(e: Engine, h: Holder): number {
    return e.applyFD(h.p, h.q);
  }
}

import { Engine } from "./engine";
import { Trace } from "./trace";

export class Holder {
  field: number = 0;

  auto: number = 0;

  static shared: number = 0;

  constructor() { }

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
    this.auto = this.auto + 1;

    return this.auto;
  }

  get bumpField(): number {
    this.field = this.field + 1;

    return this.field;
  }

  get target(): Engine {
    Trace.next("t");

    return Engine.fromDefault();
  }

  onThis(e: Engine): number {
    return (($arg1, $arg0) => e.applyFD($arg0, $arg1))(this.p, this.q);
  }
}

import { Engine } from "./engine";
import type { Holder } from "./holder";

export class Derived extends Engine {
  constructor(h: Holder) {
    const __baseArg1 = h.p;
    const __baseArg0 = h.q;
    super(Engine.byEngineFD, __baseArg0, __baseArg1);
  }
}
