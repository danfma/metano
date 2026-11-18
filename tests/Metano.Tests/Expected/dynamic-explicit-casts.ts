import { bigintFromDynamic, castTo, charFromDynamic, decimalSourceFromDynamic, numberFromDynamic } from "metano-runtime";
import { Decimal } from "metano-runtime/system/decimal";
import type { IHasArea } from "./i-has-area";
import { Shape } from "./shape";

export class Casts {
  constructor() { }

  all(d: any): void {
    const i = numberFromDynamic(d, "Int32");
    const b = numberFromDynamic(d, "Byte");
    const checkedByte = numberFromDynamic(d, "Byte", true);
    const f = numberFromDynamic(d, "Single");
    const x = numberFromDynamic(d, "Double");
    const l = bigintFromDynamic(d, "Int64");
    const u = bigintFromDynamic(d, "UInt64");
    const big = bigintFromDynamic(d, "BigInteger");
    const c = charFromDynamic(d);
    const m = new Decimal(decimalSourceFromDynamic(d));
    const maybe = d == null ? null : numberFromDynamic(d, "Int32");
    const shape = castTo<Shape>(d, Shape);
    const area = d as IHasArea;
    const text = d as string;
    const o = d;
    const color = d;
    const point = d;
    const flag = d;
  }
}
