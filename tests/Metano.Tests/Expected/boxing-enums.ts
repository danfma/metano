import { List } from "metano-runtime/system/collections";
import { box, castTo, defaultEquals, enumText, formatBoolean, getRuntimeType, isType, objectText, unbox, unboxFor } from "metano-runtime";
import { Color, Color$runtimeType } from "./color.js";

export class Probe {
  constructor() { }

  static boxed(): Object {
    return box(Color.Red, Color$runtimeType);
  }

  static questions(o: Object): string {
    const t = getRuntimeType(o);
    const s = objectText(o);
    const eq = defaultEquals(o, box(Color.Red, Color$runtimeType));
    const c = castTo<Color>(o, Color$runtimeType) as Color;
    let k: Color;

    if (isType(o, Color$runtimeType) && (k = unbox(o) as Color, true) && k === Color.Green) {
      return "green";
    }

    {
      let $s = o;

      if (unboxFor($s, Color$runtimeType) === Color.Red) {
        return "red";
      }

      if (isType($s, Color$runtimeType)) {
        const other = unbox($s) as Color;

        return enumText(other, Color$runtimeType);
      }

      return `${t.name} ${s ?? ""} ${formatBoolean(eq)} ${enumText(c, Color$runtimeType)}`;
    }
  }

  static raw(): List<Color> {
    return List.of<Color>([Color.Red]);
  }

  static interface(): IComparable {
    return Color.Red;
  }

  static bcl(): Object {
    return DayOfWeek.Monday;
  }

  static hole(o: Object): void {
    console.log((unbox(o) as Object));
  }
}
