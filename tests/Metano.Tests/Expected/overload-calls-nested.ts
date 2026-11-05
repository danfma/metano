import { isBool, isInt32, isString } from "metano-runtime";

export class Owner {
  protected static readonly byOwnerId: unique symbol = Symbol("Owner(int id)");

  protected static readonly byOwnerCode: unique symbol = Symbol("Owner(string code)");

  protected static readonly byOwnerFlag: unique symbol = Symbol("Owner(bool flag)");

  readonly how: string = "";

  constructor(overload: typeof Owner.byOwnerId, id: number);
  /** @internal */
  constructor(overload: typeof Owner.byOwnerCode, code: string);
  /** @internal */
  constructor(overload: typeof Owner.byOwnerFlag, flag: boolean);
  constructor(id: number);
  /** @internal */
  constructor(code: string);
  /** @internal */
  constructor(flag: boolean);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Owner.byOwnerId) {
      const id = args[1] as number;
      this.how = "id";

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Owner.byOwnerCode) {
      const code = args[1] as string;
      this.how = code;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Owner.byOwnerFlag) {
      const flag = args[1] as boolean;
      this.how = "flag";

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const id = args[0] as number;
      this.how = "id";

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      const code = args[0] as string;
      this.how = code;

      return;
    }

    if (args.length === 1 && isBool(args[0])) {
      const flag = args[0] as boolean;
      this.how = "flag";

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromId(id: number): Owner {
    return new Owner(Owner.byOwnerId, id);
  }

  static fromCode(code: string): Owner {
    return new Owner(Owner.byOwnerCode, code);
  }

  private static fromFlag(flag: boolean): Owner {
    return new Owner(Owner.byOwnerFlag, flag);
  }
}

export namespace Owner {
  export class Maker {
    constructor() { }

    make(): Owner {
      return Owner.fromCode("made");
    }
  }
}
