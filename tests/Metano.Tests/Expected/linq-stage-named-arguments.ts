import { aggregate, firstOrDefault, lastOrDefault, linq, select, singleOrDefault, toArray, toMap, where, zip } from "metano-runtime/system/linq";

export class Stages {
  private _count: number = 0;

  constructor() { }

  get seed(): number {
    return this.next();
  }

  get fn(): (arg1: number, arg2: number) => number {
    return this.adder();
  }

  next(): number {
    this._count = this._count + 1;

    return this._count;
  }

  adder(): (arg1: number, arg2: number) => number {
    return (a: number, x: number) => a + x;
  }

  matcher(): (arg: number) => boolean {
    return (x: number) => x > 1;
  }

  picker(): (arg: number) => number {
    return (x: number) => x * 10;
  }

  others(): number[] {
    return ((receiver$temp$0) => {
      receiver$temp$0.push(10);
      receiver$temp$0.push(20);

      return receiver$temp$0;
    })([] as Array<number>);
  }

  nextAsync(): Promise<number> {
    return Promise.resolve(this.next());
  }

  load(): Promise<number[]> {
    return Promise.resolve(this.others());
  }

  aggregate(xs: number[]): number {
    return linq(xs, aggregate(0, (a: number, x: number) => a + x));
  }

  toDictionary(xs: number[]): Map<number, number> {
    return linq(xs, toMap((x: number) => x, (x: number) => x * 10));
  }

  zip(xs: number[], ys: number[]): number[] {
    return linq(xs, zip(ys, (a: number, b: number) => a + b), toArray());
  }

  firstOrDefault(xs: number[]): number {
    return linq(xs, firstOrDefault((x: number) => x > 1, 42));
  }

  oneEffect(xs: number[]): number {
    return linq(xs, aggregate(this.next(), (a: number, x: number) => a + x));
  }

  inOrder(xs: number[]): number {
    return linq(xs, aggregate(this.next(), this.adder()));
  }

  positionalThenNamed(xs: number[]): Map<number, number> {
    return linq(xs, toMap((x: number) => x, this.picker()));
  }

  twoEffects(xs: number[]): number {
    return linq(xs, (($arg1, $arg0) => aggregate($arg0, $arg1))(this.adder(), this.next()));
  }

  computedReads(xs: number[]): number {
    return linq(xs, (($arg1, $arg0) => aggregate($arg0, $arg1))(this.fn, this.seed));
  }

  midPipe(xs: number[]): number[] {
    return linq(xs, where((x: number) => x > 0), (($arg1, $arg0) => zip($arg0, $arg1))(this.adder(), this.others()), select((x: number) => x + 1), toArray());
  }

  first(xs: number[]): number {
    return linq(xs, (($arg1, $arg0) => firstOrDefault($arg0, $arg1))(this.next(), this.matcher()));
  }

  last(xs: number[]): number {
    return linq(xs, (($arg1, $arg0) => lastOrDefault($arg0, $arg1))(this.next(), this.matcher()));
  }

  single(xs: number[]): number {
    return linq(xs, (($arg1, $arg0) => singleOrDefault($arg0, $arg1))(this.next(), this.matcher()));
  }

  defaultOnly(xs: number[]): number {
    return linq(xs, firstOrDefault(undefined, this.next()));
  }

  nullConditional(xs: number[] | null): number | null {
    return xs == null ? null : linq(xs, (($arg1, $arg0) => aggregate($arg0, $arg1))(this.adder(), this.next()));
  }

  async afterAwait(): Promise<number> {
    return linq(await this.load(), (($arg1, $arg0) => aggregate($arg0, $arg1))(this.adder(), await this.nextAsync()));
  }

  queryableZip(q: Iterable<number>): number[] {
    return linq(q, zip(this.others(), (a: number, b: number) => a + b, {
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
    }), toArray());
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

  queryableToDictionary(q: Iterable<number>): Map<number, number> {
    return linq(q, toMap((x: number) => x, (x: number) => x * 10, {
      tree: {
        kind: "param",
        name: "x",
        type: { name: "number" },
      },
    }));
  }
}
