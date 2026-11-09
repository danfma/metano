import { isBool, isInt32 } from "metano-runtime";
import { Base } from "./base";

export class Hider extends Base {
  constructor() {
    super();
  }

  addX$(x: number): string {
    return "hidden";
  }

  addB(b: boolean): string {
    return "bool";
  }

  override add(s: string): string;
  override add(x: number): string;
  override add(b: boolean): string;
  override add(...args: unknown[]): string {
    if (args.length === 1 && isInt32(args[0])) {
      return this.addX$(args[0] as number);
    }

    if (args.length === 1 && isBool(args[0])) {
      return this.addB(args[0] as boolean);
    }

    return super.add(...args as [never]);
  }
}
