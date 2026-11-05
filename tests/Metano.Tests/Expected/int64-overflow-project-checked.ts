import { checkedInt64, checkedUInt64 } from "metano-runtime";

export class Project {
  private _total: bigint = 0n;

  constructor() { }

  add(l: bigint, m: bigint): bigint {
    return checkedInt64(l + m);
  }

  uSub(u: bigint, v: bigint): bigint {
    return checkedUInt64(u - v);
  }

  neg(l: bigint): bigint {
    return checkedInt64(-l);
  }

  wrapped(l: bigint): bigint {
    return BigInt.asIntN(64, l + 1n);
  }

  step(l: bigint): bigint {
    l = checkedInt64(l + 1n);
    this._total = checkedInt64(this._total + l);

    return this._total;
  }

  narrow(a: number): number {
    return a + 1;
  }
}
