import { checkedNarrowNumber } from "metano-runtime";

export const B = {
  One: 1,
} as const;

export type B = typeof B[keyof typeof B];

export class EnumArithmetic {
  private constructor() { }

  static add(b: B, n: number): B {
    return ((b + n) & 0xFF) as B;
  }

  static subtract(b: B, n: number): B {
    return ((b - n) & 0xFF) as B;
  }

  static difference(a: B, b: B): number {
    return (a - b) & 0xFF;
  }

  static checkedAdd(b: B, n: number): B {
    return checkedNarrowNumber(b + n, 8, false) as B;
  }

  static checkedSubtract(b: B, n: number): B {
    return checkedNarrowNumber(b - n, 8, false) as B;
  }

  static postfix(b: B): B {
    return (((b = ((b + 1) & 0xFF) as B) - 1) & 0xFF) as B;
  }

  static prefix(b: B): B {
    return b = ((b + 1) & 0xFF) as B;
  }

  static compound(b: B, n: number): B {
    b = ((b + n) & 0xFF) as B;

    return b;
  }

  static checkedCompound(b: B, n: number): B {
    {
      b = checkedNarrowNumber(b + n, 8, false) as B;
    }

    return b;
  }

  static checkedIncrement(b: B): B {
    {
      b = checkedNarrowNumber(b + 1, 8, false) as B;
    }

    return b;
  }

  static innerUnchecked(b: B): B {
    return ((b + 1) & 0xFF) as B;
  }

  static lifted(b: B | null, n: number | null): B | null {
    return b == null || n == null ? null : ((b + n) & 0xFF) as B;
  }

  static liftedCompound(b: B | null, n: number | null): B | null {
    b = b == null || n == null ? null : ((b + n) & 0xFF) as B;

    return b;
  }

  static liftedPostfix(b: B | null): B | null {
    return ((receiver$temp$0) => {
      b = receiver$temp$0 == null ? null : ((receiver$temp$0 + 1) & 0xFF) as B;

      return receiver$temp$0;
    })(b);
  }

  static checkedLifted(b: B | null, n: number | null): B | null {
    return b == null || n == null ? null : checkedNarrowNumber(b + n, 8, false) as B;
  }
}
