import { intDiv, isInt32, isInt64 } from "metano-runtime";

export class Ops {
  static readonly max: number = UInt64.maxValue;

  static readonly small: number = 42;

  static readonly lMax: number = Int64.maxValue;

  field: number = 0;

  initialized: number = 7;

  lField: number = 0;

  constructor() { }

  add(a: number, b: number): number {
    return a + b;
  }

  shift(a: number, n: number): number {
    return a >> n;
  }

  div(a: number, b: number): number {
    return intDiv(a, b);
  }

  def(x: number = 5, y: number = 18446744073709551615): number {
    return x + y;
  }

  lit(): number {
    return 18446744073709551615;
  }

  zero(): number {
    return undefined;
  }

  is(o: Object): boolean {
    return typeof o === "number";
  }

  private overLong(x: number): string {
    return "long";
  }

  private overLong(x: number): string {
    return "ulong";
  }

  private overInt(x: number): string {
    return "int";
  }

  over(x: number): string;
  over(x: number): string;
  over(x: number): string;
  over(...args: unknown[]): string {
    if (args.length === 1 && isInt64(args[0])) {
      return this.overLong(args[0] as number);
    }

    if (args.length === 1 && isInt64(args[0])) {
      return this.overLong(args[0] as number);
    }

    if (args.length === 1 && isInt32(args[0])) {
      return this.overInt(args[0] as number);
    }

    throw new Error("No matching overload for over");
  }

  inc(a: number): number {
    a++;
    a += 2;

    return a;
  }

  lifted(a: number | null): number | null {
    return a == null ? null : a + 1;
  }

  mixed(a: number, b: number): number {
    return a * b;
  }

  toD(a: number): number {
    return a;
  }

  toL(a: number): number {
    return a;
  }

  fromL(a: number): number {
    return a;
  }

  narrow(a: number): number {
    return a;
  }

  sw(a: number): number {
    return a === 0 ? 1 : a === 1 ? 2 : a;
  }

  pat(a: number): boolean {
    return a > 5 && a < 10;
  }

  fmt(a: number): string {
    return `${a} ${a.toString()}`;
  }

  cmp(a: number, b: number): number {
    return a.compareTo(b);
  }

  arr(): number[] {
    return [1, 2, 3];
  }

  sum(xs: number[]): number {
    let s = 0;
    for (const x of xs) {
      s += x;
    }

    return s;
  }

  lambda(): number {
    const f = (x: number) => x * 2;

    return f(3);
  }
}
