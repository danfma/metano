// slots/index.ts
export { IntList } from "./int-list";
export { Point } from "./point";
export { Probe } from "./probe";

// slots/int-list.ts
import { List } from "metano-runtime/system/collections";
import { Int32Type } from "metano-runtime";

export class IntList extends List<number> {
  constructor() {
    super(null, [Int32Type]);
  }
}

// slots/point.ts
import { HashCode, copyRecordWith } from "metano-runtime";

export class Point {
  constructor(readonly x: number, readonly y: number) { }

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

// slots/probe.ts
import { Dictionary, List, Queue, iReadOnlyListType, listType, type IReadOnlyList } from "metano-runtime/system/collections";
import { Int32Type, isType, StringType } from "metano-runtime";
import { linq, toList } from "metano-runtime/system/linq";
import { Point } from "./point";

export class Probe {
  constructor() { }

  static isList(o: Object): boolean {
    return isType(o, listType(StringType));
  }

  static isReadOnly(o: List<Object>): boolean {
    return isType(o, iReadOnlyListType(StringType));
  }

  static covariant(xs: string[]): IReadOnlyList<Object> {
    return List.adopt<Object>(xs, { fixedSize: true }, [StringType]);
  }

  static make(): List<number> {
    return List.of<number>([1, 2], [Int32Type]);
  }

  static empty(): List<number> {
    return new List<number>(null, [Int32Type]);
  }

  static points(): Dictionary<string, Point> {
    return Dictionary.assign<string, Point>([
      ["a", new Point(1, 2)],
    ]);
  }

  static linq(xs: Iterable<number>): List<number> {
    return linq(xs, toList([Int32Type]));
  }

  static unasked(): Queue<number> {
    return new Queue<number>();
  }
}
