import { checkedInt64, checkedUInt64 } from "metano-runtime";

export class Counter {
  private readonly _slots: bigint[];

  private _field: bigint = 0n;

  private _slotReads: number = 0;

  total: bigint = 0n;

  constructor(seed: bigint) {
    this._field = seed;
    this._slots = [0n, seed];
    this.total = seed;
  }

  incrementField(): bigint {
    this._field = BigInt.asIntN(64, this._field + 1n);

    return this._field;
  }

  addToTotal(amount: bigint): bigint {
    this.total = BigInt.asIntN(64, this.total + amount);

    return this.total;
  }

  multiplySlot(factor: bigint): bigint {
    ((receiver$temp$0) => this._slots[receiver$temp$0] = BigInt.asIntN(64, this._slots[receiver$temp$0] * factor))(this.nextSlot());

    return this._slotReads === 1 ? this._slots[1] : -1n;
  }

  incrementSlot(): bigint {
    const previous = ((receiver$temp$0) => BigInt.asIntN(64, (this._slots[receiver$temp$0] = BigInt.asIntN(64, this._slots[receiver$temp$0] + 1n)) - 1n))(this.nextSlot());

    return this._slotReads === 1 ? previous : -1n;
  }

  private nextSlot(): number {
    this._slotReads++;

    return 1;
  }
}

const FnvPrime: bigint = 1099511628211n;

export function add(a: bigint, b: bigint): bigint {
  return BigInt.asIntN(64, a + b);
}

export function subtract(a: bigint, b: bigint): bigint {
  return BigInt.asIntN(64, a - b);
}

export function multiply(a: bigint, b: bigint): bigint {
  return BigInt.asIntN(64, a * b);
}

export function negate(a: bigint): bigint {
  return BigInt.asIntN(64, -a);
}

export function complement(a: bigint): bigint {
  return ~a;
}

export function addInt(a: bigint, b: number): bigint {
  return BigInt.asIntN(64, a + BigInt(b));
}

export function addUnsigned(a: bigint, b: bigint): bigint {
  return BigInt.asUintN(64, a + b);
}

export function subtractUnsigned(a: bigint, b: bigint): bigint {
  return BigInt.asUintN(64, a - b);
}

export function multiplyUnsigned(a: bigint, b: bigint): bigint {
  return BigInt.asUintN(64, a * b);
}

export function complementUnsigned(a: bigint): bigint {
  return BigInt.asUintN(64, ~a);
}

export function addChecked(a: bigint, b: bigint): bigint {
  return checkedInt64(a + b);
}

export function subtractChecked(a: bigint, b: bigint): bigint {
  return checkedInt64(a - b);
}

export function multiplyChecked(a: bigint, b: bigint): bigint {
  return checkedInt64(a * b);
}

export function negateChecked(a: bigint): bigint {
  return checkedInt64(-a);
}

export function addIntChecked(a: bigint, b: number): bigint {
  return checkedInt64(a + BigInt(b));
}

export function addUnsignedChecked(a: bigint, b: bigint): bigint {
  return checkedUInt64(a + b);
}

export function subtractUnsignedChecked(a: bigint, b: bigint): bigint {
  return checkedUInt64(a - b);
}

export function multiplyUnsignedChecked(a: bigint, b: bigint): bigint {
  return checkedUInt64(a * b);
}

export function complementUnsignedChecked(a: bigint): bigint {
  return BigInt.asUintN(64, ~a);
}

export function promotedAdd(a: number, b: number): bigint {
  return BigInt.asIntN(64, BigInt(a) + BigInt(b));
}

export function promotedMultiply(a: number, b: number): bigint {
  return BigInt.asIntN(64, BigInt(a) * BigInt(b));
}

export function promotedNegate(a: number): bigint {
  return BigInt.asIntN(64, -BigInt(a));
}

export function promotedNarrow(a: number, b: number): number {
  return Number(BigInt.asIntN(32, BigInt.asIntN(64, BigInt(a) + BigInt(b))));
}

export function liftedAdd(a: bigint | null, b: bigint | null): bigint | null {
  return a == null || b == null ? null : BigInt.asIntN(64, a + b);
}

export function liftedMultiplyChecked(a: bigint | null, b: bigint | null): bigint | null {
  return a == null || b == null ? null : checkedInt64(a * b);
}

export function liftedNegate(a: bigint | null): bigint | null {
  return a == null ? null : BigInt.asIntN(64, -a);
}

export function liftedComplement(a: bigint | null): bigint | null {
  return a == null ? null : BigInt.asUintN(64, ~a);
}

export function checkedBlock(a: bigint, b: bigint): bigint {
  {
    const sum = checkedInt64(a + b);

    return checkedInt64(sum * 2n);
  }
}

export function uncheckedInsideChecked(a: bigint, b: bigint): bigint {
  {
    return checkedInt64(BigInt.asIntN(64, a * b) - 1n);
  }
}

export function checkedLambda(a: bigint): bigint {
  {
    const next = (x: bigint) => checkedInt64(x + 1n);

    return next(a);
  }
}

export function prefixIncrement(a: bigint): bigint {
  let x = a;

  return x = BigInt.asIntN(64, x + 1n);
}

export function postfixIncrement(a: bigint): bigint {
  let x = a;

  return BigInt.asIntN(64, (x = BigInt.asIntN(64, x + 1n)) - 1n);
}

export function postfixIncrementStored(a: bigint): bigint {
  let x = a;
  x = BigInt.asIntN(64, x + 1n);

  return x;
}

export function prefixDecrement(a: bigint): bigint {
  let x = a;

  return x = BigInt.asIntN(64, x - 1n);
}

export function postfixDecrement(a: bigint): bigint {
  let x = a;

  return BigInt.asIntN(64, (x = BigInt.asIntN(64, x - 1n)) + 1n);
}

export function postfixIncrementUnsigned(a: bigint): bigint {
  let x = a;

  return BigInt.asUintN(64, (x = BigInt.asUintN(64, x + 1n)) - 1n);
}

export function prefixDecrementUnsigned(a: bigint): bigint {
  let x = a;

  return x = BigInt.asUintN(64, x - 1n);
}

export function postfixIncrementChecked(a: bigint): bigint {
  let x = a;

  return (x = checkedInt64(x + 1n)) - 1n;
}

export function prefixDecrementChecked(a: bigint): bigint {
  let x = a;

  return x = checkedInt64(x - 1n);
}

export function postfixDecrementUnsignedChecked(a: bigint): bigint {
  let x = a;

  return (x = checkedUInt64(x - 1n)) + 1n;
}

export function compoundAdd(a: bigint, b: bigint): bigint {
  let x = a;
  x = BigInt.asIntN(64, x + b);

  return x;
}

export function compoundSubtractInt(a: bigint, b: number): bigint {
  let x = a;
  x = BigInt.asIntN(64, x - BigInt(b));

  return x;
}

export function compoundMultiplyChecked(a: bigint, b: bigint): bigint {
  let x = a;

  {
    x = checkedInt64(x * b);
  }

  return x;
}

export function compoundAddUnsigned(a: bigint, b: bigint): bigint {
  let x = a;
  x = BigInt.asUintN(64, x + b);

  return x;
}

export function liftedIncrement(a: bigint | null): bigint | null {
  let x = a;
  x = x == null ? null : BigInt.asIntN(64, x + 1n);

  return x;
}

export function liftedCompoundAdd(a: bigint | null, b: bigint): bigint | null {
  let x = a;
  x = x == null ? null : BigInt.asIntN(64, x + b);

  return x;
}

export function fieldIncrement(a: bigint): bigint {
  return new Counter(a).incrementField();
}

export function propertyCompound(a: bigint, b: bigint): bigint {
  return new Counter(a).addToTotal(b);
}

export function elementCompound(a: bigint, b: bigint): bigint {
  return new Counter(a).multiplySlot(b);
}

export function elementPostfix(a: bigint): bigint {
  return new Counter(a).incrementSlot();
}

export function hashMix(seed: bigint, value: bigint): bigint {
  return BigInt.asIntN(64, (seed ^ value) * FnvPrime);
}
