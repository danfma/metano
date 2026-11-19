/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { box, defaultEquals, getRuntimeType, isBoxOf, type RuntimeType } from "metano-runtime";
import { Color, Color$runtimeType } from "./color";

export class Boxing {
  constructor() {}

  static boxRed(): Object {
    return box(Color.Red, Color$runtimeType);
  }

  static typeOf(value: Object): RuntimeType {
    return getRuntimeType(value);
  }

  static textOf(value: Object): string {
    return value.toString() ?? "";
  }

  static isRed(value: Object): boolean {
    return defaultEquals(value, box(Color.Red, Color$runtimeType));
  }

  static isColor(value: Object): boolean {
    return isBoxOf(value, Color$runtimeType);
  }

  static colors(): Color[] {
    return [Color.Red, Color.Green];
  }
}
