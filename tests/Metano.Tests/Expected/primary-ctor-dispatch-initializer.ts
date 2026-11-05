import { isString } from "metano-runtime";

export class Init {
  protected static readonly byInitName: unique symbol = Symbol("Init(string name)");

  protected static readonly byInitDefault: unique symbol = Symbol("Init()");

  readonly name: string;

  constructor(overload: typeof Init.byInitName, name: string);
  constructor(overload: typeof Init.byInitDefault);
  constructor(name: string);
  constructor();
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Init.byInitName) {
      const name = args[1] as string;
      this.name = name;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Init.byInitDefault) {
      const __chain0Arg0 = "d";

      {
        const name = __chain0Arg0;
        this.name = name;
      }

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      const name = args[0] as string;
      this.name = name;

      return;
    }

    if (args.length === 0) {
      const __chain0Arg0 = "d";

      {
        const name = __chain0Arg0;
        this.name = name;
      }

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromName(name: string): Init {
    return new Init(Init.byInitName, name);
  }

  static fromDefault(): Init {
    return new Init(Init.byInitDefault);
  }
}
