import { valueEquals } from "metano-runtime";
import { Decimal } from "decimal.js";
import type { Counter } from "./counter";
import type { Dto } from "./dto";
import type { Node } from "./node";
import type { Point } from "./point";
import type { Size } from "./size";
import type { UserId } from "./user-id";

export class Comparisons {
  constructor() { }

  records(p: Point, q: Point | null): boolean {
    return valueEquals(p, q);
  }

  recordsDiffer(p: Point, q: Point): boolean {
    return !valueEquals(p, q);
  }

  recordStructs(a: Size, b: Size): boolean {
    return valueEquals(a, b);
  }

  liftedRecordStructs(a: Size | null, b: Size): boolean {
    return valueEquals(a, b);
  }

  plainObjects(a: Dto, b: Dto): boolean {
    return valueEquals(a, b);
  }

  brandedPrimitives(a: UserId, b: UserId): boolean {
    return a === b && a === b && 1 === 1;
  }

  nullCheck(p: Point | null): boolean {
    return p == null || null != p;
  }

  references(a: Node, b: Node): boolean {
    return a === b;
  }

  tuples(a: [number, string], b: [number, string]): boolean {
    return a[0] === b[0] && a[1] === b[1];
  }

  tuplesDiffer(a: [number, number]): boolean {
    return a[0] !== 1 || a[1] !== 2;
  }

  tupleOfRecords(a: [Point, number], b: [Point, Node]): boolean {
    return valueEquals(a[0], b[0]) && valueEquals(a[0], b[0]) && b[1] === b[1];
  }

  nestedTuples(a: [[number, number], number]): boolean {
    return a[0][0] === 1 && a[0][1] === 2 && a[1] === 3;
  }

  evaluatedOnce(c: Counter): boolean {
    return ((tuple$temp$0) => ((tuple$temp$1) => tuple$temp$0[0] === tuple$temp$1 && tuple$temp$0[1] === 10)(c.next()))(c.pair());
  }

  decimalElements(a: [Decimal, number], d: Decimal): boolean {
    return a[0].eq(d) && a[1] === 1 && (1 !== 1 || !new Decimal("2").eq(d));
  }

  tupleEqualsMethod(a: [number, number], b: [number, number]): boolean {
    return valueEquals(a, b);
  }
}
