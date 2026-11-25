import { Decimal } from "metano-runtime/system/decimal";
import { Bag } from "./bag.js";
import { Box } from "./box.js";
import { Money } from "./money.js";
import { Pt } from "./pt.js";
import { Single } from "./single.js";

export class Shop {
  constructor() { }

  static price(): Money {
    return Money.fromAmount(new Decimal("19.9"));
  }

  static cents(): Money {
    return Money.fromCents(1990);
  }

  static coded(): Money {
    return Money.fromCodeScale("BRL", 2);
  }

  static named(): Money {
    return Money.fromCodeScale("USD", 3);
  }

  static boxed(): Box<string> {
    return Box.fromValue<string>("gift");
  }

  static counted(): Box<string> {
    return Box.fromCount<string>(2);
  }

  static expanded(): number {
    return Bag.fromItems(1, 2, 3).count;
  }

  static empty(): number {
    return Bag.fromItems().count;
  }

  static spread(items: number[]): number {
    return Bag.fromItems(...items).count;
  }

  static zero(): number {
    return new Pt().x;
  }

  static pair(): number {
    return Pt.fromXY(1, 2).x;
  }

  static one(): Single {
    return new Single(1);
  }
}
