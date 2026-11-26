import { isBool, isChar, isFloat64, isInt32, isString } from "metano-runtime";

export class Acct {
  protected static readonly byAcctId = /* @__PURE__ */ Symbol("Acct(int id)");
  protected static readonly byAcctCode = /* @__PURE__ */ Symbol("Acct(string code)");
  protected static readonly byAcctIdCode = /* @__PURE__ */ Symbol("Acct(int id, string code)");
  protected static readonly byAcctFlag = /* @__PURE__ */ Symbol("Acct(bool flag)");
  protected static readonly byAcctRate = /* @__PURE__ */ Symbol("Acct(double rate)");
  protected static readonly byAcctTag = /* @__PURE__ */ Symbol("Acct(char tag)");
  declare readonly how: string;

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
      this.how = "id";

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Acct.byAcctCode) {
      this.how = "code";

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Acct.byAcctIdCode) {
      this.how = "idcode";

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Acct.byAcctFlag) {
      this.how = "flag";

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Acct.byAcctRate) {
      this.how = "rate";

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Acct.byAcctTag) {
      this.how = "tag";

      return;
    }

    if (args.length === 2 && isInt32(args[0]) && isString(args[1])) {
      this.how = "idcode";

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      this.how = "id";

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      this.how = "code";

      return;
    }

    if (args.length === 1 && isBool(args[0])) {
      this.how = "flag";

      return;
    }

    if (args.length === 1 && isFloat64(args[0])) {
      this.how = "rate";

      return;
    }

    if (args.length === 1 && isChar(args[0])) {
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
