import { isBool, isInt32, isString } from "metano-runtime";
import type { IQ } from "./iq.js";
import { QB } from "./qb.js";

export class QD extends QB implements IQ<number>, IQ<string> {
  constructor() {
    super();
  }

  override qBq(z: number): string {
    return "di";
  }

  qQ$(q: boolean): string {
    return "db";
  }

  override q(z: number, n: number): string;
  override q(s: string, m: number): string;
  override q(z: number): string;
  override q(s: string): string;
  override q(q: boolean): string;
  override q(...args: unknown[]): string {
    if (args.length === 1 && isBool(args[0])) {
      return this.qQ$(args[0] as boolean);
    }

    return super.q(...args as [never]);
  }

  qQ(a: number): string;
  qQ(a: string): string;
  qQ(...args: unknown[]): string {
    if (args.length === 1 && isInt32(args[0])) {
      return this.qBq(args[0] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return this.qS(args[0] as string);
    }

    throw new Error("No matching overload for qQ");
  }

  qAN(a: number, n: number): string;
  qAN(a: string, n: number): string;
  qAN(...args: unknown[]): string {
    if (args.length === 2 && isInt32(args[0]) && isInt32(args[1])) {
      return this.qZN(args[0] as number, args[1] as number);
    }

    if (args.length === 2 && isString(args[0]) && isInt32(args[1])) {
      return this.qSM(args[0] as string, args[1] as number);
    }

    throw new Error("No matching overload for qAN");
  }
}
