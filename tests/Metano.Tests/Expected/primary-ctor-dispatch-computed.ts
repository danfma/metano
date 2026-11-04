import { isString } from "metano-runtime";

export class Deps {
  readonly name: string;

  private readonly _len: number = 0;

  constructor(name: string);
  constructor();
  constructor(...args: unknown[]) {
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
}
