import { isInt32 } from "metano-runtime";
import { Box } from "./box";

export class IntBox extends Box<number> {
  protected static readonly byIntBoxValue: unique symbol = Symbol("IntBox(int value)");

  protected static readonly byIntBoxValueExtra: unique symbol = Symbol("IntBox(int value, int extra)");

  constructor(overload: typeof IntBox.byIntBoxValue, value: number);
  constructor(overload: typeof IntBox.byIntBoxValueExtra, value: number, extra: number);
  constructor(value: number, extra: number);
  constructor(value: number);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === IntBox.byIntBoxValue) {
      const value = args[1] as number;
      super(value);

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === IntBox.byIntBoxValueExtra) {
      const value = args[1] as number;
      const extra = args[2] as number;
      super(value + extra);

      return;
    }

    if (args.length === 2 && isInt32(args[0]) && isInt32(args[1])) {
      const value = args[0] as number;
      const extra = args[1] as number;
      super(value + extra);

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const value = args[0] as number;
      super(value);

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromIntBoxValue(value: number): IntBox {
    return new IntBox(IntBox.byIntBoxValue, value);
  }

  static fromValueExtra(value: number, extra: number): IntBox {
    return new IntBox(IntBox.byIntBoxValueExtra, value, extra);
  }
}
