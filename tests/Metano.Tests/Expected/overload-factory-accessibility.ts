import { isBool, isChar, isFloat64, isInt32, isString } from "metano-runtime";

export class Acct {
  protected static readonly byAcctId: unique symbol = Symbol("Acct(int id)");

  protected static readonly byAcctCode: unique symbol = Symbol("Acct(string code)");

  protected static readonly byAcctIdCode: unique symbol = Symbol("Acct(int id, string code)");

  protected static readonly byAcctFlag: unique symbol = Symbol("Acct(bool flag)");

  protected static readonly byAcctRate: unique symbol = Symbol("Acct(double rate)");

  protected static readonly byAcctTag: unique symbol = Symbol("Acct(char tag)");

  readonly how: string = "";

  constructor(overload: typeof Acct.byAcctId, id: number);
  /** @internal */
  constructor(overload: typeof Acct.byAcctCode, code: string);
  constructor(overload: typeof Acct.byAcctIdCode, id: number, code: string);
  /** @internal */
  constructor(overload: typeof Acct.byAcctFlag, flag: boolean);
  constructor(overload: typeof Acct.byAcctRate, rate: number);
  /** @internal */
  constructor(overload: typeof Acct.byAcctTag, tag: string);
  constructor(id: number, code: string);
  constructor(id: number);
  /** @internal */
  constructor(code: string);
  /** @internal */
  constructor(flag: boolean);
  constructor(rate: number);
  /** @internal */
  constructor(tag: string);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Acct.byAcctId) {
      const id = args[1] as number;
      this.how = "id";

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Acct.byAcctCode) {
      const code = args[1] as string;
      this.how = "code";

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Acct.byAcctIdCode) {
      const id = args[1] as number;
      const code = args[2] as string;
      this.how = "idcode";

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Acct.byAcctFlag) {
      const flag = args[1] as boolean;
      this.how = "flag";

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Acct.byAcctRate) {
      const rate = args[1] as number;
      this.how = "rate";

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Acct.byAcctTag) {
      const tag = args[1] as string;
      this.how = "tag";

      return;
    }

    if (args.length === 2 && isInt32(args[0]) && isString(args[1])) {
      const id = args[0] as number;
      const code = args[1] as string;
      this.how = "idcode";

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const id = args[0] as number;
      this.how = "id";

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      const code = args[0] as string;
      this.how = "code";

      return;
    }

    if (args.length === 1 && isBool(args[0])) {
      const flag = args[0] as boolean;
      this.how = "flag";

      return;
    }

    if (args.length === 1 && isFloat64(args[0])) {
      const rate = args[0] as number;
      this.how = "rate";

      return;
    }

    if (args.length === 1 && isChar(args[0])) {
      const tag = args[0] as string;
      this.how = "tag";

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromId(id: number): Acct {
    return new Acct(Acct.byAcctId, id);
  }

  private static fromCode(code: string): Acct {
    return new Acct(Acct.byAcctCode, code);
  }

  protected static fromIdCode(id: number, code: string): Acct {
    return new Acct(Acct.byAcctIdCode, id, code);
  }

  /** @internal */
  static fromFlag(flag: boolean): Acct {
    return new Acct(Acct.byAcctFlag, flag);
  }

  static fromRate(rate: number): Acct {
    return new Acct(Acct.byAcctRate, rate);
  }

  /** @internal */
  protected static fromTag(tag: string): Acct {
    return new Acct(Acct.byAcctTag, tag);
  }

  static parse(code: string): Acct {
    return Acct.fromCode(code);
  }
}
