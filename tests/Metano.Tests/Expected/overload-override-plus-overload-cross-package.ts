import { isBool } from "metano-runtime";
import { Base } from "lib/lib";

export class Derived extends Base {
  constructor() {
    super();
  }

  override addX(x: number): string {
    return "derived";
  }

  addB(b: boolean): string {
    return "bool";
  }

  override add(x: number): string;
  override add(s: string): string;
  override add(b: boolean): string;
  override add(...args: unknown[]): string {
    if (args.length === 1 && isBool(args[0])) {
      return this.addB(args[0] as boolean);
    }

    return super.add(...args as [never]);
  }

  mapBC(b: boolean, c: boolean): string {
    return "flags";
  }

  override mapX<T>(x: T): string {
    return "derived";
  }

  override map(x: number, y: number): string;
  override map<T>(x: T): string;
  override map(b: boolean, c: boolean): string;
  override map(...args: unknown[]): any {
    if (args.length === 2 && isBool(args[0]) && isBool(args[1])) {
      return this.mapBC(args[0] as boolean, args[1] as boolean);
    }

    return super.map(...args as [never]);
  }
}
