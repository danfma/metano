import { HashCode } from "metano-runtime";

export class Hashes {
  constructor() { }

  one(a: number): number {
    return HashCode.combine(a);
  }

  two(a: number, b: string): number {
    return HashCode.combine(a, b);
  }

  eight(a: number, b: number, c: number, d: number, e: number, f: number, g: number, h: number): number {
    return HashCode.combine(a, b, c, d, e, f, g, h);
  }

  accumulate(a: number, b: string): number {
    const hash = new HashCode();
    hash.add(a);
    hash.add(b);

    return hash.toHashCode();
  }
}
