/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { enumFromRaw, enumIsDefined } from "metano-runtime";
import { Color$runtimeType, type Color } from "./color.js";

export class Palette {
  constructor() {}

  static isDefined(value: number): boolean {
    return enumIsDefined(Color$runtimeType, value);
  }

  static parse(text: string): Color {
    return enumFromRaw(Color$runtimeType, text);
  }

  static parseIgnoringCase(text: string): Color {
    return enumFromRaw(Color$runtimeType, text, true);
  }

  static fromRaw(raw: number): Object {
    return enumFromRaw(Color$runtimeType, raw);
  }
}
