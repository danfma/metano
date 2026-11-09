import { isBool } from "metano-runtime";
import { Base } from "./base";

export class Derived extends Base {
  constructor() {
    super();
  }

  override addX(x: number): number {
    return x + 1;
  }

  addBoolean(b: boolean): number {
    return 1;
  }

  override add(x: number): number;
  override add(s: string): number;
  override add(b: boolean): number;
  override add(...args: unknown[]): number {
    if (args.length === 1 && isBool(args[0])) {
      return this.addBoolean(args[0] as boolean);
    }

    return super.add(...args as [never]);
  }
}
