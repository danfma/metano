import { isInt32, isString } from "metano-runtime";

export class Ledger {
  protected static readonly byLedgerSeed: unique symbol = Symbol("Ledger(int seed)");

  protected static readonly byLedgerName: unique symbol = Symbol("Ledger(string name)");

  constructor(overload: typeof Ledger.byLedgerSeed, seed: number);
  /** @internal */
  constructor(overload: typeof Ledger.byLedgerName, name: string);
  constructor(seed: number);
  /** @internal */
  constructor(name: string);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Ledger.byLedgerSeed) {
      const seed = args[1] as number;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Ledger.byLedgerName) {
      const name = args[1] as string;

      return;
    }

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

  static fromSeed(seed: number): Ledger {
    return new Ledger(Ledger.byLedgerSeed, seed);
  }

  /** @internal */
  static fromName(name: string): Ledger {
    return new Ledger(Ledger.byLedgerName, name);
  }

  /** @internal */
  postAmountMemo(amount: number, memo: string): number {
    return amount + memo.length;
  }

  postAmount(amount: number): number {
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

  auditAmount(amount: number): number {
    return amount;
  }

  protected auditMemo(memo: string): number {
    return memo.length;
  }

  audit(amount: number): number;
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
