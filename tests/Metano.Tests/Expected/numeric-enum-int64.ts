export const Wide = {
  None: 0n,
  Low: 1n,
  High: 1099511627776n,
  Top: 4611686018427387904n,
} as const;

export type Wide = bigint & { readonly __enum?: "Wide" };

export const Huge = {
  Zero: 0n,
  Top: 9223372036854775808n,
  Max: 18446744073709551615n,
} as const;

export type Huge = typeof Huge[keyof typeof Huge];

export const Rank = {
  First: 1n,
  Second: 2n,
} as const;

export type Rank = typeof Rank[keyof typeof Rank];

export const Small = {
  A: 1,
  B: 2,
} as const;

export type Small = typeof Small[keyof typeof Small];

export class Ops {
  private constructor() { }

  static combine(): Wide {
    return Wide.Low | Wide.High;
  }

  static mask(w: Wide): Wide {
    return w & ~Wide.Low ^ Wide.Top;
  }

  static hasHigh(w: Wide): boolean {
    return (w & Wide.High) !== 0n;
  }

  static hasTop(w: Wide): boolean {
    return (w & Wide.Top) === Wide.Top;
  }

  static invert(h: Huge): Huge {
    return BigInt.asUintN(64, ~h) as Huge;
  }

  static next(h: Huge): Huge {
    return BigInt.asUintN(64, h + 1n) as Huge;
  }

  static step(r: Rank): Rank {
    r = BigInt.asIntN(64, r + 1n) as Rank;
    r = BigInt.asIntN(64, r + 2n) as Rank;

    return r;
  }

  static distance(a: Rank, b: Rank): bigint {
    return BigInt.asIntN(64, a - b);
  }

  static shifted(w: Wide): Wide {
    return BigInt.asIntN(64, w << 2n);
  }

  static toLong(w: Wide): bigint {
    return w;
  }

  static toInt(w: Wide): number {
    return Number(BigInt.asIntN(32, w));
  }

  static toULong(w: Wide): bigint {
    return BigInt.asUintN(64, w);
  }

  static fromHuge(h: Huge): bigint {
    return BigInt.asIntN(64, h);
  }

  static toDouble(h: Huge): number {
    return Number(h);
  }

  static fromLong(l: bigint): Wide {
    return l;
  }

  static fromInt(i: number): Huge {
    return BigInt.asUintN(64, BigInt(i)) as Huge;
  }

  static toSmall(r: Rank): Small {
    return Number(BigInt.asIntN(32, r)) as Small;
  }

  static fromSmall(s: Small): Rank {
    return BigInt(s) as Rank;
  }

  static zero(): Wide {
    return 0n;
  }

  static noZero(): Rank {
    return 0n as Rank;
  }

  static ranks(): Rank[] {
    return new Array<Rank>(2).fill(0n as Rank);
  }

  static lifted(w: Wide | null): Wide | null {
    return w == null ? null : w | Wide.Low;
  }

  static before(a: Huge): boolean {
    return a < Huge.Max && a !== Huge.Top;
  }

  static label(h: Huge): string {
    return h === Huge.Zero ? "zero" : h === Huge.Max ? "max" : "other";
  }
}
