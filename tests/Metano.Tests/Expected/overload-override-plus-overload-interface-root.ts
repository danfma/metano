import { isBool, isInt32 } from "metano-runtime";
import type { IQ } from "./iq";
import { QB } from "./qb";

export class QD extends QB implements IQ<number>, IQ<string> {
  constructor() {
    super();
  }

  qQ$(q: boolean): string {
    return "db";
  }

  qQ(a: number): string;
  qQ(a: string): string;
  qQ(...args: unknown[]): string {
    if (args.length === 1 && isInt32(args[0])) {
      const z = args[0] as number;

      return "di";
    }

    throw new Error("No matching overload for qQ");
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
}
