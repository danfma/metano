import { bigintFromDynamic, BooleanType, castTo, charFromDynamic, decimalSourceFromDynamic, numberFromDynamic, StringType } from "metano-runtime";
import { Decimal } from "metano-runtime/system/decimal";
import { Color$runtimeType, type Color } from "./color.js";
import { IHasArea$runtimeType, type IHasArea } from "./i-has-area.js";
import { Point } from "./point.js";
import { Shape } from "./shape.js";

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
    const area = castTo<IHasArea>(d, IHasArea$runtimeType);
    const text = castTo<string>(d, StringType);
    const o = d;
    const color = castTo<Color>(d, Color$runtimeType) as Color;
    const point = castTo<Point>(d, Point);
    const flag = castTo<boolean>(d, BooleanType);
  }
}
