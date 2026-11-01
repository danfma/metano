import { Box } from "./box";
import type { Celsius } from "./celsius";
import { Point } from "./point";
import { Size } from "./size";

export class Segment {
  start: Point = new Point();

  origin: Point | null = null;

  boxed: Box<number> = new Box();

  extent: Size = new Size();

  temperature: Celsius;

  fixed: Point = ((receiver$temp$0) => {
    receiver$temp$0.x = 1;

    return receiver$temp$0;
  })(new Point());

  constructor() { }
}
