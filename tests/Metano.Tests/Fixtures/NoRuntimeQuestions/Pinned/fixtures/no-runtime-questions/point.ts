import { HashCode, absInt32, copyRecordWith } from "metano-runtime";

export class Point {
  declare readonly x: number;
  declare readonly y: number;

  constructor(x: number, y: number) {
    this.x = x;
    this.y = y;
  }

  translate(dx: number, dy: number): Point {
    return this.with({ x: (this.x + dx) | 0, y: (this.y + dy) | 0 });
  }

  manhattanLength(): number {
    return (absInt32(this.x) + absInt32(this.y)) | 0;
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
