import { isString } from "metano-runtime";

export class Deps {
  protected static readonly byDepsName: unique symbol = Symbol("Deps(string name)");

  protected static readonly byDepsDefault: unique symbol = Symbol("Deps()");

  readonly name: string;

  private readonly _len: number = 0;

  constructor(overload: typeof Deps.byDepsName, name: string);
  constructor(overload: typeof Deps.byDepsDefault);
  constructor(name: string);
  constructor();
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Deps.byDepsName) {
      const name = args[1] as string;
      this.name = name;
      this._len = name.length;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Deps.byDepsDefault) {
      const __chain0Arg0 = "default";

      {
        const name = __chain0Arg0;
        this.name = name;
        this._len = name.length;
      }

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      const name = args[0] as string;
      this.name = name;
      this._len = name.length;

      return;
    }

    if (args.length === 0) {
      const __chain0Arg0 = "default";

      {
        const name = __chain0Arg0;
        this.name = name;
        this._len = name.length;
      }

      return;
    }

    throw new Error("No matching constructor");
  }

  get len(): number {
    return this._len;
  }

  static fromName(name: string): Deps {
    return new Deps(Deps.byDepsName, name);
  }

  static fromDefault(): Deps {
    return new Deps(Deps.byDepsDefault);
  }
}
