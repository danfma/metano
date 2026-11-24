// wire/i-has-label.ts
import { implementsInterface, type InterfaceType } from "metano-runtime";
import { Tagged$runtimeType } from "./tagged.js";

export interface IHasLabel {
  readonly label: string;
}

export const IHasLabel$runtimeType: InterfaceType<IHasLabel> = {
  kind: "interface",
  name: "IHasLabel",
  fullName: "Wire.IHasLabel",
  namespace: "Wire",
  baseInterfaces: [],
  plainImplementers: [
    () => Tagged$runtimeType,
  ],
  test: (v: unknown): v is IHasLabel => implementsInterface(v, IHasLabel$runtimeType),
};

// wire/index.ts
export { IHasLabel$runtimeType } from "./i-has-label.js";
export type { IHasLabel } from "./i-has-label.js";
export { isPoint, Point$runtimeType } from "./point.js";
export type { Point } from "./point.js";
export { Probe } from "./probe.js";
export { isTagged, Tagged$runtimeType } from "./tagged.js";
export type { Tagged } from "./tagged.js";
export type { Unasked } from "./unasked.js";

// wire/point.ts
import type { PlainObjectType } from "metano-runtime";

export interface Point {
  readonly x: number;
  readonly y: number;
}

export function isPoint(value: unknown): value is Point {
  if (value == null || typeof value !== "object") {
    return false;
  }

  const v = value as any;

  return typeof v.x === "number" && typeof v.y === "number";
}

export const Point$runtimeType: PlainObjectType<Point> = {
  kind: "plainObject",
  name: "Point",
  fullName: "Wire.Point",
  namespace: "Wire",
  guard: isPoint,
  test: isPoint,
};

// wire/probe.ts
import { castTo, getRuntimeType, isString, isType } from "metano-runtime";
import { IHasLabel$runtimeType } from "./i-has-label.js";
import { Point$runtimeType, isPoint, type Point } from "./point.js";
import { isTagged } from "./tagged.js";

export class Probe {
  constructor() { }

  static isPoint(o: Object): boolean {
    return isPoint(o);
  }

  static asPoint(o: Object): Point | null {
    return isPoint(o) ? o : null;
  }

  static toPoint(o: Object): Point {
    return castTo<Point>(o, Point$runtimeType);
  }

  static kind(o: Object): number {
    {
      let $s = o;

      if (isPoint($s)) {
        return 1;
      }

      if (isTagged($s)) {
        const t = $s;

        return t.count;
      }

      return 0;
    }
  }

  static hasLabel(o: Object): boolean {
    return isType(o, IHasLabel$runtimeType);
  }

  static typeName(p: Point): string {
    return (getRuntimeType(p), Point$runtimeType).name;
  }

  static make(): Point {
    return { x: 1, y: 2 };
  }

  static madeTypeName(): string {
    return (getRuntimeType(Probe.make()), Point$runtimeType).name;
  }

  static newTypeName(): string {
    return Point$runtimeType.name;
  }

  static newWithEffectTypeName(): string {
    return (getRuntimeType({ x: Probe.compute(), y: 2 }), Point$runtimeType).name;
  }

  private static compute(): number {
    return 1;
  }

  static overP(p: Point): string {
    return "point";
  }

  static overS(s: string): string {
    return s;
  }

  static over(p: Point): string;
  static over(s: string): string;
  static over(...args: unknown[]): string {
    if (args.length === 1 && isPoint(args[0])) {
      return Probe.overP(args[0] as Point);
    }

    if (args.length === 1 && isString(args[0])) {
      return Probe.overS(args[0] as string);
    }

    throw new Error("No matching overload for over");
  }
}

// wire/tagged.ts
import type { PlainObjectType } from "metano-runtime";
import type { IHasLabel } from "./i-has-label.js";

export interface Tagged extends IHasLabel {
  readonly label: string;
  readonly count: number;
}

export function isTagged(value: unknown): value is Tagged {
  if (value == null || typeof value !== "object") {
    return false;
  }

  const v = value as any;

  return typeof v.label === "string" && typeof v.count === "number";
}

export const Tagged$runtimeType: PlainObjectType<Tagged> = {
  kind: "plainObject",
  name: "Tagged",
  fullName: "Wire.Tagged",
  namespace: "Wire",
  guard: isTagged,
  test: isTagged,
};

// wire/unasked.ts
export interface Unasked {
  readonly note: string;
}
