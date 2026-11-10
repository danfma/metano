import { tryGetValue, tryParseInt32 } from "metano-runtime";
import { MoreTags } from "./more-tags";
import type { OutHost } from "./out-host";
import { Trace } from "./trace";

export class OutCalls {
  constructor() { }

  static outFirstInOrder(o: OutHost): number {
    const [r, x] = o.outFirst(Trace.next("p"), Trace.next("q"));

    return r * 100 + x;
  }

  static outFirstReordered(o: OutHost): number {
    const [r, x] = (($arg1, $arg0) => o.outFirst($arg0, $arg1))(Trace.next("p"), Trace.next("q"));

    return r * 100 + x;
  }

  static outMid(o: OutHost): number {
    const [r, x] = (($arg1, $arg0) => o.outMid($arg0, $arg1))(Trace.next("p"), Trace.next("q"));

    return r * 100 + x;
  }

  static outMidInOrder(o: OutHost): number {
    const [r, x] = o.outMid(Trace.next("p"), Trace.next("q"));

    return r * 100 + x;
  }

  static outLast(o: OutHost): number {
    const [ok, x] = (($arg1, $arg0) => o.tryLast($arg0, $arg1))(Trace.next("p"), Trace.next("q"));

    return ok ? x : -1;
  }

  static ifHead(o: OutHost): number {
    const [$found0, x] = (($arg1, $arg0) => o.tryMid($arg0, $arg1))(Trace.next("p"), Trace.next("q"));

    if ($found0) {
      return x;
    }

    return -1;
  }

  static ifHeadLast(o: OutHost): number {
    const [$found0, x] = (($arg1, $arg0) => o.tryLast($arg0, $arg1))(Trace.next("p"), Trace.next("q"));

    if ($found0) {
      return x;
    }

    return -1;
  }

  static echo(tag: string): readonly [result: number, seen: number] {
    let seen: number;
    seen = Trace.next(tag);

    return [seen, seen];
  }

  static ifHeadNested(o: OutHost): number {
    const [$result0, first] = OutCalls.echo("p");
    const [$result1, second] = OutCalls.echo("q");
    const [$found2, x] = o.tryMid($result1, $result0);

    if ($found2) {
      return x * 100 + first * 10 + second;
    }

    return -1;
  }

  static voidOut(o: OutHost): number {
    const [x] = o.voidOut(Trace.next("p"), Trace.next("q"));

    return x;
  }

  static voidOutReordered(o: OutHost): number {
    const [x] = (($arg1, $arg0) => o.voidOut($arg0, $arg1))(Trace.next("p"), Trace.next("q"));

    return x;
  }

  static twoOuts(o: OutHost): number {
    const [r, x, y] = (($arg1, $arg0) => o.two($arg0, $arg1))(Trace.next("p"), Trace.next("q"));

    return r * 100 + x * 10 + y;
  }

  static existingLocal(o: OutHost): number {
    let x: number;
    const [r, $out0] = o.outFirst(Trace.next("p"), Trace.next("q"));
    x = $out0;

    return r * 100 + x;
  }

  static outFirstSingle(o: OutHost): number {
    const [r, x] = o.outFirst(Trace.next("p"), 3);

    return r * 100 + x;
  }

  static tryGetValue(d: Map<string, number>, key: string): number {
    const [ok, v] = tryGetValue(d, key, 0);

    return ok ? v : -1;
  }

  static tryGetValueCall(d: Map<string, number>): number {
    const [$found0, v] = tryGetValue(d, MoreTags.tag("k"), 0);

    if ($found0) {
      return v;
    }

    return -1;
  }

  static tryParse(): number {
    const [ok, n] = tryParseInt32(MoreTags.tag("12"));

    return ok ? n : -1;
  }
}

import { Overloaded } from "./overloaded";
import { Trace } from "./trace";

export class OverloadedOutCalls {
  constructor() { }

  static firstInOrder(o: Overloaded): number {
    const [r, x] = o.firstOutOfInt32Int32Int32(Trace.next("p"), Trace.next("q"));

    return r * 1000 + x;
  }

  static firstReordered(o: Overloaded): number {
    const [r, x] = (($arg1, $arg0) => o.firstOutOfInt32Int32Int32($arg0, $arg1))(Trace.next("p"), Trace.next("q"));

    return r * 1000 + x;
  }

  static midInOrder(o: Overloaded): number {
    const [r, x] = o.midInt32OutOfInt32Int32(Trace.next("p"), Trace.next("q"));

    return r * 1000 + x;
  }

  static midReordered(o: Overloaded): number {
    const [r, x] = (($arg1, $arg0) => o.midInt32OutOfInt32Int32($arg0, $arg1))(Trace.next("p"), Trace.next("q"));

    return r * 1000 + x;
  }

  static lastReordered(o: Overloaded): number {
    const [r, x] = (($arg1, $arg0) => o.lastInt32Int32OutOfInt32($arg0, $arg1))(Trace.next("p"), Trace.next("q"));

    return r * 1000 + x;
  }

  static staticInOrder(): number {
    const [r, x] = Overloaded.sharedOutOfInt32Int32Int32(Trace.next("p"), Trace.next("q"));

    return r * 1000 + x;
  }

  static staticReordered(): number {
    const [r, x] = (($arg1, $arg0) => Overloaded.sharedOutOfInt32Int32Int32($arg0, $arg1))(Trace.next("p"), Trace.next("q"));

    return r * 1000 + x;
  }
}
