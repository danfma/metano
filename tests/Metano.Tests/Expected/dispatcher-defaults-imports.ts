import { isBool, isInt32, isString } from "metano-runtime";
import { Decimal } from "decimal.js";
import { Color } from "./color";

export class Painter {
  protected static readonly byPainterC: unique symbol = Symbol("Painter(Color c)");

  protected static readonly byPainterFlag: unique symbol = Symbol("Painter(bool flag)");

  readonly log: string = "";

  constructor(overload: typeof Painter.byPainterC, c?: Color);
  constructor(overload: typeof Painter.byPainterFlag, flag: boolean);
  constructor(flag: boolean);
  constructor(c?: Color);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Painter.byPainterC) {
      const c = (args[1] === undefined ? Color.Green : args[1]) as Color;
      this.log = "color:" + c;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Painter.byPainterFlag) {
      const flag = args[1] as boolean;
      this.log = "bool";

      return;
    }

    if (args.length === 1 && isBool(args[0])) {
      const flag = args[0] as boolean;
      this.log = "bool";

      return;
    }

    if (args.length <= 1 && (args[0] === undefined || isInt32(args[0]))) {
      const c = (args[0] === undefined ? Color.Green : args[0]) as Color;
      this.log = "color:" + c;

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromC(c: Color = Color.Green): Painter {
    return new Painter(Painter.byPainterC, c);
  }

  static fromFlag(flag: boolean): Painter {
    return new Painter(Painter.byPainterFlag, flag);
  }

  dB(b: boolean): string {
    return "bool";
  }

  dC(c: Color = Color.Green): string {
    return "color:" + c;
  }

  d(b: boolean): string;
  d(c?: Color): string;
  d(...args: unknown[]): string {
    if (args.length === 1 && isBool(args[0])) {
      return this.dB(args[0] as boolean);
    }

    if (args.length <= 1 && (args[0] === undefined || isInt32(args[0]))) {
      return this.dC(args[0] as Color);
    }

    throw new Error("No matching overload for d");
  }

  static fS(s: string): string {
    return "s";
  }

  static fAD(a: number, d: Decimal = new Decimal("1.5")): string {
    return "dec:" + d;
  }

  static f(s: string): string;
  static f(a: number, d?: Decimal): string;
  static f(...args: unknown[]): string {
    if (args.length === 1 && isString(args[0])) {
      return Painter.fS(args[0] as string);
    }

    if (args.length >= 1 && args.length <= 2 && isInt32(args[0]) && (args[1] === undefined || args[1] instanceof Decimal)) {
      return Painter.fAD(args[0] as number, args[1] as Decimal);
    }

    throw new Error("No matching overload for f");
  }
}
