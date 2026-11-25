import { HashCode, valueEquals } from "metano-runtime";
import { Box } from "./box.js";
import type { Celsius } from "./celsius.js";
import type { Counter } from "./counter.js";
import { Point } from "./point.js";
import { Size } from "./size.js";

export class Segment {
  declare start: Point;
  declare origin: Point | null;
  declare boxed: Box<number>;
  declare extent: Size;
  declare temperature: Celsius;
  declare tally: Counter;
  declare fixed: Point;

  constructor() {
    this.start = new Point();
    this.origin = null;
    this.boxed = new Box<number>();
    this.extent = new Size();
    this.temperature = undefined!;
    this.tally = undefined!;
    this.fixed = ((receiver$temp$0) => {
      receiver$temp$0.x = 1;

      return receiver$temp$0;
    })(new Point());
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && valueEquals(this.start, other.start) && valueEquals(this.origin, other.origin) && valueEquals(this.boxed, other.boxed) && valueEquals(this.extent, other.extent) && valueEquals(this.temperature, other.temperature) && valueEquals(this.tally, other.tally) && valueEquals(this.fixed, other.fixed);
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.start);
    hc.add(this.origin);
    hc.add(this.boxed);
    hc.add(this.extent);
    hc.add(this.temperature);
    hc.add(this.tally);
    hc.add(this.fixed);

    return hc.toHashCode();
  }
}
