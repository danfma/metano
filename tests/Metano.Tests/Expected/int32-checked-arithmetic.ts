import { checkedInt32, checkedUInt32, intDiv } from "metano-runtime";

export class Checks {
  total: number = 0;

  constructor() { }

  add(a: number, b: number): number {
    return checkedInt32(a + b);
  }

  sub(a: number, b: number): number {
    return checkedInt32(a - b);
  }

  mul(a: number, b: number): number {
    return checkedInt32(a * b);
  }

  neg(a: number): number {
    return checkedInt32(-a);
  }

  uAdd(a: number, b: number): number {
    return checkedUInt32(a + b);
  }

  uSub(a: number, b: number): number {
    return checkedUInt32(a - b);
  }

  uMul(a: number, b: number): number {
    return checkedUInt32(a * b);
  }

  bytes(a: number, b: number): number {
    return a + b;
  }

  shorts(a: number, b: number): number {
    return a * b;
  }

  uShorts(a: number, b: number): number {
    return checkedInt32(a * b);
  }

  negShort(a: number): number {
    return -a;
  }

  shift(a: number): number {
    return a << 1;
  }

  divide(a: number, b: number): number {
    return intDiv(a, b);
  }

  lifted(a: number | null, b: number | null): number | null {
    return a == null || b == null ? null : checkedInt32(a + b);
  }

  step(a: number): number {
    {
      a = checkedInt32(a + 1);
      a = checkedInt32(a + 1);
      a = checkedInt32(a - 1);
      a = checkedInt32(a + 5);
      a = checkedInt32(a - 1);
      a = checkedInt32(a * 2);
    }

    return a;
  }

  postfix(a: number): number {
    {
      const b = (a = checkedInt32(a + 1)) - 1;

      return b;
    }
  }

  uStep(u: number): number {
    {
      u = checkedUInt32(u + 1);
      u = checkedUInt32(u - 1);
    }

    return u;
  }

  onField(): void {
    {
      this.total = checkedInt32(this.total + 1);
      this.total = checkedInt32(this.total + 2);
    }
  }

  inner(a: number, b: number): number {
    return checkedInt32(a + a * b);
  }

  plain(a: number, b: number): number {
    return a + b * a - 1;
  }
}
