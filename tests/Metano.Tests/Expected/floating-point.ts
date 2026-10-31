export class Numbers {
  constructor() { }

  static isBroken(v: number): boolean {
    return Number.isNaN(v) || (Math.abs(v) === Infinity);
  }

  static isFallbackInfinite(a: number | null, b: number): boolean {
    return (Math.abs(a ?? b) === Infinity);
  }

  static isNotOverflow(a: number, b: number): boolean {
    return !Object.is(a - b, Infinity);
  }

  static scale(x: number): number {
    return x * 3.4028234663852886e+38;
  }

  static lowest(): number {
    return -(-Number.MAX_VALUE);
  }

  static isMissing(v: number | null): boolean {
    return Number.isNaN(v);
  }

  static isPresent(v: number): boolean {
    return !Number.isNaN(v);
  }

  static equalsNaN(v: number): boolean {
    return v === Number.NaN;
  }

  static describe(v: number): string {
    switch (true) {
      case Number.isNaN(v):
        return "missing";
      case v === 0:
      case v === Infinity:
        return "edge";
      default:
        return "value";
    }
  }
}
