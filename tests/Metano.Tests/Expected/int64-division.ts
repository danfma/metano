import { checkedInt64, divInt64, divUInt64, intDiv, remInt64, remUInt64 } from "metano-runtime";

export class Divide {
  private _cursor: number = 0;

  total: bigint = 0n;

  slots: bigint[] = [1n, 2n, 3n];

  constructor() { }

  next(): number {
    return this._cursor++;
  }

  div(l: bigint, m: bigint): bigint {
    return divInt64(l, m);
  }

  rem(l: bigint, m: bigint): bigint {
    return remInt64(l, m);
  }

  uDiv(u: bigint, v: bigint): bigint {
    return divUInt64(u, v);
  }

  uRem(u: bigint, v: bigint): bigint {
    return remUInt64(u, v);
  }

  divConst(l: bigint): bigint {
    return l / 7n;
  }

  remConst(l: bigint): bigint {
    return l % 7n;
  }

  divMinusOne(l: bigint): bigint {
    return divInt64(l, -1n);
  }

  remMinusOne(l: bigint): bigint {
    return remInt64(l, -1n);
  }

  uDivConst(u: bigint): bigint {
    return u / 3n;
  }

  uRemMax(u: bigint): bigint {
    return u % 18446744073709551615n;
  }

  constDividend(m: bigint): bigint {
    return divInt64(100n, m);
  }

  divInt(l: bigint, i: number): bigint {
    return divInt64(l, BigInt(i));
  }

  checked(l: bigint, m: bigint): bigint {
    return checkedInt64(divInt64(l, m) + remInt64(l, m));
  }

  mixed(l: bigint, m: bigint): bigint {
    return BigInt.asIntN(64, BigInt.asIntN(64, divInt64(l, m) * 2n) + remInt64(l, m));
  }

  native(a: bigint, b: bigint): bigint {
    return divInt64(a, b);
  }

  lifted(nl: bigint | null, nm: bigint | null): bigint | null {
    return nl == null || nm == null ? null : divInt64(nl, nm);
  }

  liftedRem(nl: bigint | null, m: bigint): bigint | null {
    return nl == null ? null : remInt64(nl, m);
  }

  narrow(a: number, b: number): number {
    return intDiv(a, b) + a % b;
  }

  compound(l: bigint, m: bigint): bigint {
    l = divInt64(l, m);
    l = remInt64(l, m);
    l = l / 4n;
    l = l % 3n;

    return l;
  }

  uCompound(u: bigint, v: bigint): bigint {
    u = divUInt64(u, v);
    u = remUInt64(u, v);

    return u;
  }

  liftedCompound(nl: bigint | null, m: bigint): bigint | null {
    nl = nl == null ? null : divInt64(nl, m);
    nl = nl == null ? null : remInt64(nl, m);

    return nl;
  }

  members(m: bigint): void {
    this.total = divInt64(this.total, m);
    ((receiver$temp$0) => this.slots[receiver$temp$0] = remInt64(this.slots[receiver$temp$0], m))(this.next());
  }
}
