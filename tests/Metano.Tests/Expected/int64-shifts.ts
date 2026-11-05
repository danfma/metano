export class Shift {
  private _cursor: number = 0;

  total: bigint = 0n;

  slots: bigint[] = [1n, 2n, 3n];

  constructor() { }

  next(): number {
    return this._cursor++;
  }

  left(l: bigint, n: number): bigint {
    return BigInt.asIntN(64, l << BigInt(n & 63));
  }

  leftConst(l: bigint): bigint {
    return BigInt.asIntN(64, l << 3n);
  }

  leftMasked(l: bigint): bigint {
    return BigInt.asIntN(64, l << 6n);
  }

  leftNegative(l: bigint): bigint {
    return BigInt.asIntN(64, l << 63n);
  }

  uLeft(u: bigint, n: number): bigint {
    return BigInt.asUintN(64, u << BigInt(n & 63));
  }

  right(l: bigint, n: number): bigint {
    return l >> BigInt(n & 63);
  }

  uRight(u: bigint, n: number): bigint {
    return u >> BigInt(n & 63);
  }

  rightConst(l: bigint): bigint {
    return l >> 0n;
  }

  unsigned(l: bigint): bigint {
    return BigInt.asIntN(64, BigInt.asUintN(64, l) >> 2n);
  }

  unsignedVar(l: bigint, n: number): bigint {
    return BigInt.asIntN(64, BigInt.asUintN(64, l) >> BigInt(n & 63));
  }

  uUnsigned(u: bigint): bigint {
    return u >> 2n;
  }

  uUnsignedVar(u: bigint, n: number): bigint {
    return u >> BigInt(n & 63);
  }

  checkedLeft(l: bigint): bigint {
    return BigInt.asIntN(64, l << 1n);
  }

  byteCount(l: bigint, b: number): bigint {
    return BigInt.asIntN(64, l << BigInt(b & 63));
  }

  expression(l: bigint, n: number): bigint {
    return BigInt.asIntN(64, l << BigInt(n + 1 & 63));
  }

  lifted(nl: bigint | null, n: number): bigint | null {
    return nl == null ? null : BigInt.asIntN(64, nl << BigInt(n & 63));
  }

  liftedCount(l: bigint, n: number | null): bigint | null {
    return n == null ? null : BigInt.asIntN(64, l << BigInt(n & 63));
  }

  bitwise(l: bigint, m: bigint): bigint {
    return l & m | l ^ ~m;
  }

  narrow(i: number, n: number): number {
    return i << n | i >> 2 | i >>> n;
  }

  compound(l: bigint, n: number): bigint {
    l = BigInt.asIntN(64, l << BigInt(n & 63));
    l = l >> 2n;
    l = BigInt.asIntN(64, BigInt.asUintN(64, l) >> BigInt(n & 63));

    return l;
  }

  uCompound(u: bigint, n: number): bigint {
    u = BigInt.asUintN(64, u << 1n);
    u = u >> BigInt(n & 63);
    u = u >> 3n;

    return u;
  }

  checkedCompound(l: bigint): bigint {
    {
      l = BigInt.asIntN(64, l << 1n);
    }

    return l;
  }

  members(n: number): void {
    this.total = BigInt.asIntN(64, this.total << 2n);
    ((receiver$temp$0) => this.slots[receiver$temp$0] = BigInt.asIntN(64, BigInt.asUintN(64, this.slots[receiver$temp$0]) >> BigInt(n & 63)))(this.next());
  }
}
