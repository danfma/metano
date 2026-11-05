import { isString } from "metano-runtime";

export class Cap {
  protected static readonly byCapName: unique symbol = Symbol("Cap(string name)");

  protected static readonly byCapDefault: unique symbol = Symbol("Cap()");

  private readonly _name: string;

  constructor(overload: typeof Cap.byCapName, name: string);
  constructor(overload: typeof Cap.byCapDefault);
  constructor(name: string);
  constructor();
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Cap.byCapName) {
      const name = args[1] as string;
      this._name = name;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Cap.byCapDefault) {
      const __chain0Arg0 = "d";

      {
        const name = __chain0Arg0;
        this._name = name;
      }

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      const name = args[0] as string;
      this._name = name;

      return;
    }

    if (args.length === 0) {
      const __chain0Arg0 = "d";

      {
        const name = __chain0Arg0;
        this._name = name;
      }

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromName(name: string): Cap {
    return new Cap(Cap.byCapName, name);
  }

  static fromDefault(): Cap {
    return new Cap(Cap.byCapDefault);
  }

  greet(): string {
    return "hi " + this._name;
  }
}
