import { checkedInt64 } from "metano-runtime";

export class Counter {
  private _field: bigint = 0n;

  private _cursor: number = 0;

  total: bigint = 0n;

  unsigned: bigint = 0n;

  maybe: bigint | null = null;

  slots: bigint[] = [1n, 2n, 3n];

  constructor() { }

  next(): number {
    return this._cursor++;
  }

  locals(l: bigint, u: bigint): bigint {
    l = BigInt.asIntN(64, l + 1n);
    l = BigInt.asIntN(64, l + 1n);
    l = BigInt.asIntN(64, l - 1n);
    l = BigInt.asIntN(64, l - 1n);
    u = BigInt.asUintN(64, u + 1n);
    u = BigInt.asUintN(64, u - 1n);

    return BigInt.asIntN(64, l + BigInt.asIntN(64, u));
  }

  postfix(l: bigint): bigint {
    const old = BigInt.asIntN(64, (l = BigInt.asIntN(64, l + 1n)) - 1n);

    return old;
  }

  prefix(l: bigint): bigint {
    const updated = l = BigInt.asIntN(64, l + 1n);

    return updated;
  }

  postfixDecrement(l: bigint): bigint {
    const old = BigInt.asIntN(64, (l = BigInt.asIntN(64, l - 1n)) + 1n);

    return BigInt.asIntN(64, old - l);
  }

  members(i: number): void {
    this._field = BigInt.asIntN(64, this._field + 1n);
    this.total = BigInt.asIntN(64, this.total - 1n);
    this.total = BigInt.asIntN(64, this.total + BigInt(i));
    this.unsigned = BigInt.asUintN(64, this.unsigned * 3n);
    this._field = BigInt.asIntN(64, this._field - this.total);
  }

  element(): bigint {
    ((receiver$temp$0) => this.slots[receiver$temp$0] = BigInt.asIntN(64, this.slots[receiver$temp$0] * 2n))(this.next());
    ((receiver$temp$1) => this.slots[receiver$temp$1] = BigInt.asIntN(64, this.slots[receiver$temp$1] + 1n))(this.next());
    const old = ((receiver$temp$2) => BigInt.asIntN(64, (this.slots[receiver$temp$2] = BigInt.asIntN(64, this.slots[receiver$temp$2] + 1n)) - 1n))(this.next());

    return old;
  }

  loop(n: bigint): bigint {
    let sum = 0n;
    for (let i = 0n; i < n; i = BigInt.asIntN(64, i + 1n)) {
      sum = BigInt.asIntN(64, sum + i);
    }

    return sum;
  }

  bitwise(l: bigint, m: bigint): bigint {
    l &= m;
    l |= 4n;
    l ^= m;

    return l;
  }

  lifted(nl: bigint | null, l: bigint): bigint | null {
    nl = nl == null ? null : BigInt.asIntN(64, nl + 1n);
    nl = nl == null ? null : BigInt.asIntN(64, nl + l);
    this.maybe = this.maybe == null ? null : BigInt.asIntN(64, this.maybe - 1n);
    this.maybe = this.maybe == null ? null : BigInt.asIntN(64, this.maybe * 2n);

    return nl;
  }

  checkedBlock(l: bigint, i: number): bigint {
    {
      l = checkedInt64(l + 1n);
      l = checkedInt64(l + BigInt(i));
      l = checkedInt64(l * 2n);
      this.total = checkedInt64(this.total - 1n);
      this._field = (l = checkedInt64(l - 1n)) + 1n;
    }

    return l;
  }

  checkedLambda(l: bigint): () => bigint {
    {
      return () => l = checkedInt64(l + 1n);
    }
  }

  narrow(a: number): number {
    {
      a++;
      a += 2;
    }

    return a;
  }
}
