// MS0068 Error line 16: Factory 'Clash5.fromLabel' of 'Clash5(string s)', which [Name] pins, collides with the user member 'fromLabel'; a pinned name never takes '$', because consumers that read the type from metadata call it without seeing non-public members: rename that member or change the pin (spec 013 FR-005, FR-019).
// MS0068 Error line 18: Fast path 'Clash5.pickText' of 'Pick(string s)', which [Name] pins, collides with the user member 'pickText'; a pinned name never takes '$', because consumers that read the type from metadata call it without seeing non-public members: rename that member or change the pin (spec 013 FR-005, FR-019).
// MS0068 Error line 29: Fast path 'Clash2.pickText' of 'Pick(string s)', which [Name] pins, collides with the user member 'pickText'; a pinned name never takes '$', because consumers that read the type from metadata call it without seeing non-public members: rename that member or change the pin (spec 013 FR-005, FR-019).

import { isInt32, isString } from "metano-runtime";

export class Clash5 {
  protected static readonly byClash5N = /* @__PURE__ */ Symbol("Clash5(int n)");
  protected static readonly byClash5Label = /* @__PURE__ */ Symbol("Clash5(string s)");
  declare seen: string;

  constructor(overload: typeof Clash5.byClash5N, n: number);
  constructor(overload: typeof Clash5.byClash5Label, s: string);
  constructor(n: number);
  constructor(s: string);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Clash5.byClash5N) {
      this.seen = "int";

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Clash5.byClash5Label) {
      this.seen = "label";

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      this.seen = "int";

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      this.seen = "label";

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromN(n: number): Clash5 {
    return new Clash5(Clash5.byClash5N, n);
  }

  static fromLabel(s: string): Clash5 {
    return new Clash5(Clash5.byClash5Label, s);
  }

  /** @internal */
  static fromLabel(s: string): Clash5 {
    return Clash5.fromN(99);
  }

  /** @internal */
  pickText(g: string): string {
    return "internal:" + (g ?? "");
  }

  pickN(n: number): string {
    return "c5t:" + n;
  }

  pickText(s: string): string {
    return "c5t:" + (s ?? "");
  }

  pick(n: number): string;
  pick(s: string): string;
  pick(...args: unknown[]): string {
    if (args.length === 1 && isInt32(args[0])) {
      return this.pickN(args[0] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return this.pickText(args[0] as string);
    }

    throw new Error("No matching overload for pick");
  }
}
import { isInt32, isString } from "metano-runtime";

export class Clash2 {
  private pickText(g: string): string {
    return "private:" + (g ?? "");
  }

  pickN(n: number): string {
    return "c2:" + n;
  }

  pickText(s: string): string {
    return "c2t:" + (s ?? "");
  }

  pick(n: number): string;
  pick(s: string): string;
  pick(...args: unknown[]): string {
    if (args.length === 1 && isInt32(args[0])) {
      return this.pickN(args[0] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return this.pickText(args[0] as string);
    }

    throw new Error("No matching overload for pick");
  }
}
