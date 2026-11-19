import { box, castTo, defaultEquals, formatBoolean, getRuntimeType, isType, unbox, unboxFor } from "metano-runtime";
import { Color, Color$runtimeType } from "./color";

export class Probe {
  constructor() { }

  static boxed(): Object {
    return box(Color.Red, Color$runtimeType);
  }

  static questions(o: Object): string {
    const t = getRuntimeType(o);
    const s = o.toString();
    const eq = defaultEquals(o, box(Color.Red, Color$runtimeType));
    const c = castTo<Color>(o, Color$runtimeType) as Color;
    let k: Color;

    if (isType(o, Color$runtimeType) && (k = unbox(o) as Color, true) && k === Color.Green) {
      return "green";
    }

    return (($s) => {
      if (unboxFor($s, Color$runtimeType) === Color.Red) {
        return "red";
      }

      if (isType($s, Color$runtimeType)) {
        const other = unbox($s) as Color;

        return other.toString();
      }

      return `${t.name} ${s ?? ""} ${formatBoolean(eq)} ${c}`;
    })(o);
  }

  static raw(): Color[] {
    return [Color.Red];
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
