export const I = {
  A: 0,
  B: 1,
  C: 2,
} as const;

export type I = typeof I[keyof typeof I];

export const B = {
  Zero: 0,
  Max: 255,
} as const;

export type B = typeof B[keyof typeof B];

export const F = {
  None: 0,
  One: 1,
  Two: 2,
} as const;

export type F = number & { readonly __enum?: "F" };

export const L = {
  Zero: 0n,
  Max: 9223372036854775807n,
} as const;

export type L = typeof L[keyof typeof L];

export const U = {
  Zero: 0,
  Two: 2,
  Max: 4294967295,
} as const;

export type U = typeof U[keyof typeof U];

export class Box {
  declare mode: I;
  declare narrow: B;
  declare flags: F;
  declare wide: L;

  constructor() {
    this.mode = I.A;
    this.narrow = B.Zero;
    this.flags = F.None;
    this.wide = L.Zero;
  }

  static get(value: Box): Box {
    return value;
  }

  static idx(): number {
    return 0;
  }

  static boundedLoop(): number {
    let count = 0;
    for (let e = I.A as I; e < I.C; (e as number)++) {
      count = (count + 1) | 0;
    }

    return count;
  }

  static checkedBoundedLoop(): number {
    let count = 0;

    {
      for (let e = I.C as I; e >= I.A; (e as number)--) {
        count = (count + 1) | 0;
      }
    }

    return count;
  }

  static mutatedLoop(): void {
    for (let e = I.A as I; e < I.C; e = ((e + 1) | 0) as I) {
      e = ((e + 1) | 0) as I;
    }
  }

  static uIntLoop(): void {
    for (let e = U.Zero as U; e <= U.Two; (e as number)++) {
    }
  }

  static unboundedUIntLoop(): void {
    for (let e = U.Max as U; e <= U.Max; e = (e + 1) >>> 0 as U) {
    }
  }

  static compound(boxes: Box[], value: Box, ints: I[], bytes: B[], flags: F[], longs: L[]): void {
    const receiver$temp$0 = boxes[0]!;
    receiver$temp$0.mode = ((receiver$temp$0.mode + 1) | 0) as I;
    const receiver$temp$1 = Box.get(value);
    receiver$temp$1.mode = ((receiver$temp$1.mode - 2) | 0) as I;
    const receiver$temp$2 = Box.idx();
    ints[receiver$temp$2] = ((ints[receiver$temp$2]! + 1) | 0) as I;
    const receiver$temp$3 = boxes[0]!;
    receiver$temp$3.narrow = ((receiver$temp$3.narrow + 1) & 0xFF) as B;
    const receiver$temp$4 = Box.get(value);
    receiver$temp$4.narrow = ((receiver$temp$4.narrow - 2) & 0xFF) as B;
    const receiver$temp$5 = Box.idx();
    bytes[receiver$temp$5] = ((bytes[receiver$temp$5]! + 1) & 0xFF) as B;
    const receiver$temp$6 = boxes[0]!;
    receiver$temp$6.flags = (receiver$temp$6.flags + 1) | 0;
    const receiver$temp$7 = Box.get(value);
    receiver$temp$7.flags = (receiver$temp$7.flags - 2) | 0;
    const receiver$temp$8 = Box.idx();
    flags[receiver$temp$8] = (flags[receiver$temp$8]! + 1) | 0;
    const receiver$temp$9 = boxes[0]!;
    receiver$temp$9.wide = BigInt.asIntN(64, receiver$temp$9.wide + 1n) as L;
    const receiver$temp$10 = Box.get(value);
    receiver$temp$10.wide = BigInt.asIntN(64, receiver$temp$10.wide - 2n) as L;
    const receiver$temp$11 = Box.idx();
    longs[receiver$temp$11] = BigInt.asIntN(64, longs[receiver$temp$11]! + 1n) as L;
  }

  static increment(boxes: Box[], value: Box, ints: I[], bytes: B[], flags: F[], longs: L[]): void {
    const receiver$temp$0 = boxes[0]!;
    receiver$temp$0.mode = ((receiver$temp$0.mode + 1) | 0) as I;
    const receiver$temp$1 = Box.get(value);
    receiver$temp$1.mode = ((receiver$temp$1.mode + 1) | 0) as I;
    const receiver$temp$2 = Box.idx();
    ints[receiver$temp$2] = ((ints[receiver$temp$2]! + 1) | 0) as I;
    const receiver$temp$3 = boxes[0]!;
    receiver$temp$3.narrow = ((receiver$temp$3.narrow + 1) & 0xFF) as B;
    const receiver$temp$4 = Box.get(value);
    receiver$temp$4.narrow = ((receiver$temp$4.narrow + 1) & 0xFF) as B;
    const receiver$temp$5 = Box.idx();
    bytes[receiver$temp$5] = ((bytes[receiver$temp$5]! + 1) & 0xFF) as B;
    const receiver$temp$6 = boxes[0]!;
    receiver$temp$6.flags = (receiver$temp$6.flags + 1) | 0;
    const receiver$temp$7 = Box.get(value);
    receiver$temp$7.flags = (receiver$temp$7.flags + 1) | 0;
    const receiver$temp$8 = Box.idx();
    flags[receiver$temp$8] = (flags[receiver$temp$8]! + 1) | 0;
    const receiver$temp$9 = boxes[0]!;
    receiver$temp$9.wide = BigInt.asIntN(64, receiver$temp$9.wide + 1n) as L;
    const receiver$temp$10 = Box.get(value);
    receiver$temp$10.wide = BigInt.asIntN(64, receiver$temp$10.wide + 1n) as L;
    const receiver$temp$11 = Box.idx();
    longs[receiver$temp$11] = BigInt.asIntN(64, longs[receiver$temp$11]! + 1n) as L;
  }
}
