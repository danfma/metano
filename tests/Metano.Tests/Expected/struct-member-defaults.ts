import { HashCode, valueEquals } from "metano-runtime";
import { Box } from "./box";
import type { Celsius } from "./celsius";
import type { Counter } from "./counter";
import { Point } from "./point";
import { Size } from "./size";

export class Segment {
  start: Point = new Point();

  origin: Point | null = null;

  boxed: Box<number> = new Box<number>();

  extent: Size = new Size();

  temperature: Celsius;

  tally: Counter;

  fixed: Point = ((receiver$temp$0) => {
    receiver$temp$0.x = 1;

    return receiver$temp$0;
  })(new Point());

  constructor() { }

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
