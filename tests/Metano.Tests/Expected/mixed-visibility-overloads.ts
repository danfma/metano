import { isInt32, isString } from "metano-runtime";

export class Ledger {
  constructor(seed: number);
  /** @internal */
  constructor(name: string);
  constructor(...args: unknown[]) {
    if (args.length === 1 && isInt32(args[0])) {
      const seed = args[0] as number;

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      const name = args[0] as string;

      return;
    }

    throw new Error("No matching constructor");
  }

  private postAmountMemo(amount: number, memo: string): number {
    return amount + memo.length;
  }

  private postAmount(amount: number): number {
    return amount;
  }

  /** @internal */
  post(amount: number, memo: string): number;
  post(amount: number): number;
  post(...args: unknown[]): number {
    if (args.length === 2 && isInt32(args[0]) && isString(args[1])) {
      return this.postAmountMemo(args[0] as number, args[1] as string);
    }

    if (args.length === 1 && isInt32(args[0])) {
      return this.postAmount(args[0] as number);
    }

    throw new Error("No matching overload for post");
  }

  private auditAmount(amount: number): number {
    return amount;
  }

  private auditMemo(memo: string): number {
    return memo.length;
  }

  /** @internal */
  audit(amount: number): number;
  /** @internal */
  audit(memo: string): number;
  audit(...args: unknown[]): number {
    if (args.length === 1 && isInt32(args[0])) {
      return this.auditAmount(args[0] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return this.auditMemo(args[0] as string);
    }

    throw new Error("No matching overload for audit");
  }
}
