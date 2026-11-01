import { HashCode, copyRecordWith } from "metano-runtime";

export class Calc {
  constructor() { }

  static sum(...xs: number[]): number {
    return xs?.length ?? -1;
  }

  static join(label: string, ...parts: (string | null)[]): string {
    return label + (parts?.length ?? -1);
  }
}

export function count<T>(...items: T[]): number {
  return items?.length ?? -1;
}

export interface ISummer {
  sumAll(...xs: number[]): number;
}

export type Folder = (...xs: number[]) => number;

export class Bag {
  readonly items: number[] | null;

  constructor(...items: number[]) {
    this.items = items;
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.items === other.items;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.items);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Bag>): Bag {
    return copyRecordWith<Bag>(this, overrides);
  }
}

export class Box {
  readonly n: number = 0;

  constructor(...xs: number[]) {
    this.n = xs?.length ?? -1;
  }
}

export interface Point {
  readonly x: number;
}

export function offset(self: Point, ...deltas: number[]): number {
  return self.x + (deltas?.length ?? 0);
}
