/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { List } from "metano-runtime/system/collections";
import {
  box,
  defaultEquals,
  getRuntimeType,
  isType,
  objectText,
  type RuntimeType,
} from "metano-runtime";
import { Color, Color$runtimeType } from "./color.js";

export class Boxing {
  private constructor() {}

  static boxRed(): Object {
    return box(Color.Red, Color$runtimeType);
  }

  static typeOf(value: Object): RuntimeType {
    return getRuntimeType(value);
  }

  static textOf(value: Object): string {
    return objectText(value) ?? "";
  }

  static isRed(value: Object): boolean {
    return defaultEquals(value, box(Color.Red, Color$runtimeType));
  }

  static isColor(value: Object): boolean {
    return isType(value, Color$runtimeType);
  }

  static colors(): List<Color> {
    return List.of<Color>([Color.Red, Color.Green]);
  }
}
