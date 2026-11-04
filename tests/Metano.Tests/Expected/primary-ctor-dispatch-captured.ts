import { isString } from "metano-runtime";

export class Cap {
  private readonly _name: string;

  constructor(name: string);
  constructor();
  constructor(...args: unknown[]) {
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

  greet(): string {
    return "hi " + this._name;
  }
}
