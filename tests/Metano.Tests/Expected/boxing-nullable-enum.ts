import { box, boxNullable } from "metano-runtime";
import { Color$runtimeType, type Color } from "./color";

export class Probe {
  constructor() { }

  static identifier(maybe: Color | null): Object {
    return maybe === null ? null : box(maybe, Color$runtimeType);
  }

  static call(): Object {
    return boxNullable(Probe.maybe(), Color$runtimeType);
  }

  static maybe(): Color | null {
    return null;
  }
}
