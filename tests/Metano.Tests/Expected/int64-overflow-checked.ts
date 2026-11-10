import { checkedInt32, checkedInt64, checkedUInt64 } from "metano-runtime";

export class Check {
  constructor() { }

  add(l: bigint, m: bigint): bigint {
    return checkedInt64(l + m);
  }

  sub(l: bigint, m: bigint): bigint {
    return checkedInt64(l - m);
  }

  mul(l: bigint, m: bigint): bigint {
    return checkedInt64(l * m);
  }

  uAdd(u: bigint, v: bigint): bigint {
    return checkedUInt64(u + v);
  }

  uSub(u: bigint, v: bigint): bigint {
    return checkedUInt64(u - v);
  }

  uMul(u: bigint, v: bigint): bigint {
    return checkedUInt64(u * v);
  }

  addInt(l: bigint, i: number): bigint {
    return checkedInt64(l + BigInt(i));
  }

  neg(l: bigint): bigint {
    return checkedInt64(-l);
  }

  uNot(u: bigint): bigint {
    return BigInt.asUintN(64, ~u);
  }

  promote(ui: number, i: number): bigint {
    return checkedInt64(BigInt(ui) + BigInt(i));
  }

  lifted(nl: bigint | null): bigint | null {
    return nl == null ? null : checkedInt64(nl + 1n);
  }

  inner(l: bigint, m: bigint): bigint {
    return checkedInt64(l + BigInt.asIntN(64, l * m));
  }

  block(l: bigint, m: bigint): bigint {
    {
      const sum = checkedInt64(l + m);
      const product = BigInt.asIntN(64, l * m);

      return checkedInt64(sum - product);
    }
  }

  lambda(l: bigint): () => bigint {
    {
      return () => checkedInt64(l + 1n);
    }
  }

  nested(l: bigint): (arg: bigint) => bigint {
    {
      {
        const twice = (x: bigint) => checkedInt64(x * 2n);

        return twice;
      }
    }
  }

  narrow(a: number, b: number): number {
    return checkedInt32(a + b);
  }
}
