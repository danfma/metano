import { isBigInt, isInt32, isInt64, isUInt64 } from "metano-runtime";

export class Gates {
  constructor() { }

  pickInt32(x: number): string {
    return "int";
  }

  pickInt64(x: bigint): string {
    return "long";
  }

  pickUInt64(x: bigint): string {
    return "ulong";
  }

  pickBigInteger(x: bigint): string {
    return "bigInteger";
  }

  pick(x: number): string;
  pick(x: bigint): string;
  pick(x: bigint): string;
  pick(x: bigint): string;
  pick(...args: unknown[]): string {
    if (args.length === 1 && isInt32(args[0])) {
      return this.pickInt32(args[0] as number);
    }

    if (args.length === 1 && isInt64(args[0])) {
      return this.pickInt64(args[0] as bigint);
    }

    if (args.length === 1 && isUInt64(args[0])) {
      return this.pickUInt64(args[0] as bigint);
    }

    if (args.length === 1 && isBigInt(args[0])) {
      return this.pickBigInteger(args[0] as bigint);
    }

    throw new Error("No matching overload for pick");
  }
}
