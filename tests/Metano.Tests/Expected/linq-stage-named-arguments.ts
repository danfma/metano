import { Dictionary, List } from "metano-runtime/system/collections";
import { aggregate, firstOrDefault, lastOrDefault, linq, select, singleOrDefault, toDictionary, toList, where, zip } from "metano-runtime/system/linq";

export class Stages {
  declare private _count: number;

  constructor() {
    this._count = 0;
  }

  get seed(): number {
    return this.next();
  }

  get fn(): (arg1: number, arg2: number) => number {
    return this.adder();
  }

  next(): number {
    this._count = (this._count + 1) | 0;

    return this._count;
  }

  adder(): (arg1: number, arg2: number) => number {
    return (a: number, x: number) => (a + x) | 0;
  }

  matcher(): (arg: number) => boolean {
    return (x: number) => x > 1;
  }

  picker(): (arg: number) => number {
    return (x: number) => Math.imul(x, 10);
  }

  others(): List<number> {
    return List.of<number>([10, 20]);
  }

  nextAsync(): Promise<number> {
    return Promise.resolve(this.next());
  }

  load(): Promise<List<number>> {
    return Promise.resolve(this.others());
  }

  aggregate(xs: List<number>): number {
    return linq(xs, aggregate(0, (a: number, x: number) => (a + x) | 0));
  }

  toDictionary(xs: List<number>): Dictionary<number, number> {
    return linq(xs, toDictionary((x: number) => x, (x: number) => Math.imul(x, 10)));
  }

  zip(xs: List<number>, ys: List<number>): List<number> {
    return linq(xs, zip(ys, (a: number, b: number) => (a + b) | 0), toList());
  }

  firstOrDefault(xs: List<number>): number {
    return linq(xs, firstOrDefault((x: number) => x > 1, 42));
  }

  oneEffect(xs: List<number>): number {
    return linq(xs, aggregate(this.next(), (a: number, x: number) => (a + x) | 0));
  }

  inOrder(xs: List<number>): number {
    return linq(xs, aggregate(this.next(), this.adder()));
  }

  positionalThenNamed(xs: List<number>): Dictionary<number, number> {
    return linq(xs, toDictionary((x: number) => x, this.picker()));
  }

  twoEffects(xs: List<number>): number {
    let $arg1, $arg0;

    return linq(xs, ($arg1 = this.adder(), $arg0 = this.next(), aggregate($arg0, $arg1)));
  }

  computedReads(xs: List<number>): number {
    let $arg1, $arg0;

    return linq(xs, ($arg1 = this.fn, $arg0 = this.seed, aggregate($arg0, $arg1)));
  }

  midPipe(xs: List<number>): List<number> {
    let $arg1, $arg0;

    return linq(xs, where((x: number) => x > 0), ($arg1 = this.adder(), $arg0 = this.others(), zip($arg0, $arg1)), select((x: number) => (x + 1) | 0), toList());
  }

  first(xs: List<number>): number {
    let $arg1, $arg0;

    return linq(xs, ($arg1 = this.next(), $arg0 = this.matcher(), firstOrDefault($arg0, $arg1)));
  }

  last(xs: List<number>): number {
    let $arg1, $arg0;

    return linq(xs, ($arg1 = this.next(), $arg0 = this.matcher(), lastOrDefault($arg0, $arg1)));
  }

  single(xs: List<number>): number {
    let $arg1, $arg0;

    return linq(xs, ($arg1 = this.next(), $arg0 = this.matcher(), singleOrDefault($arg0, $arg1)));
  }

  defaultOnly(xs: List<number>): number {
    return linq(xs, firstOrDefault(undefined, this.next()));
  }

  nullConditional(xs: List<number> | null): number | null {
    let $arg1, $arg0;

    return xs == null ? null : linq(xs, ($arg1 = this.adder(), $arg0 = this.next(), aggregate($arg0, $arg1)));
  }

  async afterAwait(): Promise<number> {
    let $arg1, $arg0;

    return linq(await this.load(), ($arg1 = this.adder(), $arg0 = await this.nextAsync(), aggregate($arg0, $arg1)));
  }

  queryableZip(q: Iterable<number>): List<number> {
    return linq(q, zip(this.others(), (a: number, b: number) => (a + b) | 0, {
      tree: {
        kind: "binary",
        op: "+",
        left: {
          kind: "param",
          name: "a",
          type: { name: "number" },
        },
        right: {
          kind: "param",
          name: "b",
          type: { name: "number" },
        },
      },
    }), toList());
  }

  queryableFirst(q: Iterable<number>): number {
    return linq(q, firstOrDefault((x: number) => x > 1, this.next(), {
      tree: {
        kind: "binary",
        op: ">",
        left: {
          kind: "param",
          name: "x",
          type: { name: "number" },
        },
        right: {
          kind: "literal",
          value: 1,
          type: { name: "number" },
        },
      },
    }));
  }

  queryableToDictionary(q: Iterable<number>): Dictionary<number, number> {
    return linq(q, toDictionary((x: number) => x, (x: number) => Math.imul(x, 10), {
      tree: {
        kind: "param",
        name: "x",
        type: { name: "number" },
      },
    }));
  }
}
