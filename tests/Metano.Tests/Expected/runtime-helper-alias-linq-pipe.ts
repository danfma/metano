import { count, linq, where as where$rt } from "metano-runtime/system/linq";

export function where(x: number): boolean {
  return x > 0;
}

export class Queries {
  constructor() { }

  static positive(xs: number[]): number {
    return linq(xs, where$rt((x: number) => x > 0), count());
  }
}
