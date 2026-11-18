import { isInt32, isString, valueHashCode } from "metano-runtime";

export class Probe {
  protected static readonly byProbeDefault: unique symbol = (/* @__PURE__ */ Symbol("Probe()"));
  protected static readonly byProbeX: unique symbol = (/* @__PURE__ */ Symbol("Probe(int x)"));
  protected static readonly byProbeS: unique symbol = (/* @__PURE__ */ Symbol("Probe(string s)"));
  value: any = null;
  items: any[] = [] as Array<any>;

  constructor(overload: typeof Probe.byProbeDefault);
  constructor(overload: typeof Probe.byProbeX, x: number);
  constructor(overload: typeof Probe.byProbeS, s: string);
  constructor(x: number);
  constructor(s: string);
  constructor();
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === Probe.byProbeDefault) {
      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Probe.byProbeX) {
      const x = args[1] as number;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === Probe.byProbeS) {
      const s = args[1] as string;

      return;
    }

    if (args.length === 1 && isInt32(args[0])) {
      const x = args[0] as number;

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      const s = args[0] as string;

      return;
    }

    if (args.length === 0) {
      return;
    }

    throw new Error("No matching constructor");
  }

  static fromDefault(): Probe {
    return new Probe(Probe.byProbeDefault);
  }

  static fromX(x: number): Probe {
    return new Probe(Probe.byProbeX, x);
  }

  static fromS(s: string): Probe {
    return new Probe(Probe.byProbeS, s);
  }

  ovX(x: number): void {
  }

  ovS(s: string): void {
  }

  ov(x: number): void;
  ov(s: string): void;
  ov(...args: unknown[]): void {
    if (args.length === 1 && isInt32(args[0])) {
      this.ovX(args[0] as number);

      return;
    }

    if (args.length === 1 && isString(args[0])) {
      this.ovS(args[0] as string);

      return;
    }

    throw new Error("No matching overload for ov");
  }

  static twice(x: number): number {
    return Math.imul(x, 2);
  }

  static pickX(x: number): number {
    return x;
  }

  static pickS(s: string): number {
    return 1;
  }

  static pick(x: number): number;
  static pick(s: string): number;
  static pick(...args: unknown[]): number {
    if (args.length === 1 && isInt32(args[0])) {
      return Probe.pickX(args[0] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return Probe.pickS(args[0] as string);
    }

    throw new Error("No matching overload for pick");
  }

  members(d: any): any {
    const x = d.foo.Bar(1)[2] + d.n;
    d.AppName = "app";
    d[0] = 2;
    d.Generic();
    d(3);
    d.Count += 1;
    d[1]++;
    const maybe = d?.inner ?? null;
    const length = (z: any) => z.Length;
    const local: any = 1;
    local.Grow();

    return x;
  }

  objectMembers(d: any): void {
    const s = d.toString();
    const h = valueHashCode(d);
    const e = d.equals(1);
  }

  operators(d: any, i: number, big: bigint, text: string): void {
    const a = d + 1;
    const b = d * i;
    const c = text + d;
    const n = d == null;
    const neg = -d;
    const not = !d;
    const masked = d & 1;
    i = (i + d) | 0;
    d += 1;
    d++;
  }

  lateBound(d: any, other: Probe): void {
    other.ov(d);
    const twice = Probe.twice(d);
    const picked = Probe.pick(d);
    const created = new Probe(d);
  }
}
