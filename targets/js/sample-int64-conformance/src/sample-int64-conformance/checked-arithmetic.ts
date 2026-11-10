import { checkedInt32, checkedNarrowNumber, checkedUInt32 } from "metano-runtime";

export function intAddChecked(a: number, b: number): number {
  return checkedInt32(a + b);
}

export function intCompoundAddChecked(a: number, b: number): number {
  {
    a = checkedInt32(a + b);
  }

  return a;
}

export function intSubtractChecked(a: number, b: number): number {
  return checkedInt32(a - b);
}

export function intCompoundSubtractChecked(a: number, b: number): number {
  {
    a = checkedInt32(a - b);
  }

  return a;
}

export function intMultiplyChecked(a: number, b: number): number {
  return checkedInt32(a * b);
}

export function intCompoundMultiplyChecked(a: number, b: number): number {
  {
    a = checkedInt32(a * b);
  }

  return a;
}

export function intPrefixIncrementChecked(a: number): number {
  return a = checkedInt32(a + 1);
}

export function intPostfixIncrementChecked(a: number): number {
  {
    const previous = (a = checkedInt32(a + 1)) - 1;

    return previous;
  }
}

export function intPostfixIncrementStoredChecked(a: number): number {
  {
    a = checkedInt32(a + 1);
  }

  return a;
}

export function intPrefixDecrementChecked(a: number): number {
  return a = checkedInt32(a - 1);
}

export function intPostfixDecrementChecked(a: number): number {
  {
    const previous = (a = checkedInt32(a - 1)) + 1;

    return previous;
  }
}

export function intPostfixDecrementStoredChecked(a: number): number {
  {
    a = checkedInt32(a - 1);
  }

  return a;
}

export function uIntAddChecked(a: number, b: number): number {
  return checkedUInt32(a + b);
}

export function uIntCompoundAddChecked(a: number, b: number): number {
  {
    a = checkedUInt32(a + b);
  }

  return a;
}

export function uIntSubtractChecked(a: number, b: number): number {
  return checkedUInt32(a - b);
}

export function uIntCompoundSubtractChecked(a: number, b: number): number {
  {
    a = checkedUInt32(a - b);
  }

  return a;
}

export function uIntMultiplyChecked(a: number, b: number): number {
  return checkedUInt32(a * b);
}

export function uIntCompoundMultiplyChecked(a: number, b: number): number {
  {
    a = checkedUInt32(a * b);
  }

  return a;
}

export function uIntPrefixIncrementChecked(a: number): number {
  return a = checkedUInt32(a + 1);
}

export function uIntPostfixIncrementChecked(a: number): number {
  {
    const previous = (a = checkedUInt32(a + 1)) - 1;

    return previous;
  }
}

export function uIntPostfixIncrementStoredChecked(a: number): number {
  {
    a = checkedUInt32(a + 1);
  }

  return a;
}

export function uIntPrefixDecrementChecked(a: number): number {
  return a = checkedUInt32(a - 1);
}

export function uIntPostfixDecrementChecked(a: number): number {
  {
    const previous = (a = checkedUInt32(a - 1)) + 1;

    return previous;
  }
}

export function uIntPostfixDecrementStoredChecked(a: number): number {
  {
    a = checkedUInt32(a - 1);
  }

  return a;
}

export function intNegateChecked(a: number): number {
  return checkedInt32(-a);
}

export function intShiftLeftChecked(a: number, count: number): number {
  return a << count;
}

export function bytesAddChecked(a: number, b: number): number {
  return a + b;
}

export function shortsMultiplyChecked(a: number, b: number): number {
  return a * b;
}

export function uShortsMultiplyChecked(a: number, b: number): number {
  return checkedInt32(a * b);
}

export function shortNegateChecked(a: number): number {
  return -a;
}

export function bytesAddNarrowedChecked(a: number, b: number): number {
  return checkedNarrowNumber(a + b, 8, false);
}

export function shortsMultiplyNarrowedChecked(a: number, b: number): number {
  return checkedNarrowNumber(a * b, 16, true);
}

export function intLiftedAddChecked(a: number | null, b: number | null): number | null {
  return a == null || b == null ? null : checkedInt32(a + b);
}

export function intLiftedNegateChecked(a: number | null): number | null {
  return a == null ? null : checkedInt32(-a);
}

export function intUncheckedInsideChecked(a: number): number {
  return checkedInt32(a + (a & 0xFF));
}

export function intCheckedLambda(a: number, b: number): number {
  {
    const scale = (value: number) => checkedInt32(value * b);

    return scale(a);
  }
}

export function intFieldCompoundChecked(start: number, amount: number): number {
  const tally = new Tally(start);

  {
    tally.total = checkedInt32(tally.total + amount);
    tally.total = checkedInt32(tally.total + 1);
  }

  return tally.total;
}

export class Tally {
  total: number = 0;

  constructor(total: number) {
    this.total = total;
  }
}
