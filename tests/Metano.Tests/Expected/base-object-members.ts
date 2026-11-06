import { valueHashCode } from "metano-runtime";

export class Box {
  v: number = 0;

  constructor() { }

  equals(other: Object | null): boolean {
    let b: Box;

    return this === other && other instanceof Box && (b = other, true) && b.v === this.v;
  }

  hashCode(): number {
    return 0 ^ valueHashCode(this.v);
  }

  toString(): string {
    return "Box" + "!";
  }
}
