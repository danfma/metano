import { HashCode, copyRecordWith } from "metano-runtime";

export class Point {
  constructor(readonly x: number, readonly y: number) { }

  translate(dx: number, dy: number): Point {
    return this.with({ x: (this.x + dx) | 0, y: (this.y + dy) | 0 });
  }

  manhattanLength(): number {
    return (Math.abs(this.x) + Math.abs(this.y)) | 0;
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.x === other.x && this.y === other.y;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.x);
    hc.add(this.y);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Point>): Point {
    return copyRecordWith<Point>(this, overrides);
  }

  protected $printMembers(): string {
    return `X = ${this.x}, Y = ${this.y}`;
  }

  toString(): string {
    return `Point { ${this.$printMembers()} }`;
  }
}
