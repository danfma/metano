// shapes/circle.ts
import { HashCode, copyRecordWith, formatDouble, valueEquals } from "metano-runtime";
import { Shape, type ShapeKind } from "./shape.js";

export class Circle extends Shape {
  declare readonly radius: number;

  constructor(kind: ShapeKind, radius: number) {
    super(kind);
    this.radius = radius;
  }

  override equals(other: any): boolean {
    return super.equals(other) && valueEquals(this.radius, other.radius);
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.radius);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<Circle>): Circle {
    return copyRecordWith<Circle>(this, overrides);
  }

  protected override $printMembers(): string {
    const base = super.$printMembers?.() ?? "";

    return `${base ? `${base}, ` : ""}Radius = ${formatDouble(this.radius)}`;
  }

  override toString(): string {
    return `Circle { ${this.$printMembers()} }`;
  }
}

export function isCircle(value: unknown): value is Circle {
  if (value instanceof Circle) {
    return true;
  }

  if (value == null || typeof value !== "object") {
    return false;
  }

  const v = value as any;

  return typeof v.radius === "number" && v.kind != null;
}

export function assertCircle(value: unknown, message?: string): asserts value is Circle {
  if (!isCircle(value)) {
    throw new TypeError(message ?? "Value is not a Circle");
  }
}

// shapes/index.ts
export { assertCircle, Circle, isCircle } from "./circle.js";
export { assertShape, isShape } from "./shape.guard.js";
export { Shape, ShapeKind } from "./shape.js";
export { assertSquare, isSquare, Square } from "./square.js";

// shapes/shape.guard.ts
import { isCircle, type Circle } from "./circle.js";
import { isSquare, type Square } from "./square.js";

const variants = [
  () => isCircle,
  () => isSquare,
];

export function isShape(value: unknown): value is Circle | Square {
  if (value == null || typeof value !== "object") {
    return false;
  }

  const v = value as any;
  const variant = variants[["Circle", "Square"].indexOf(v.kind)];

  return variant !== undefined && variant()(value);
}

export function assertShape(value: unknown, message?: string): asserts value is Circle | Square {
  if (!isShape(value)) {
    throw new TypeError(message ?? "Value is not a Shape");
  }
}

// shapes/shape.ts
import { HashCode, copyRecordWith } from "metano-runtime";

export const ShapeKind = {
  Circle: "Circle",
  Square: "Square",
} as const;

export type ShapeKind = typeof ShapeKind[keyof typeof ShapeKind];

export abstract class Shape {
  declare readonly kind: ShapeKind;

  constructor(kind: ShapeKind) {
    this.kind = kind;
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.kind === other.kind;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.kind);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Shape>): Shape {
    return copyRecordWith<Shape>(this, overrides);
  }

  protected $printMembers(): string {
    return `Kind = ${this.kind}`;
  }

  toString(): string {
    return `Shape { ${this.$printMembers()} }`;
  }
}

// shapes/square.ts
import { HashCode, copyRecordWith, formatDouble, valueEquals } from "metano-runtime";
import { Shape, type ShapeKind } from "./shape.js";

export class Square extends Shape {
  declare readonly side: number;

  constructor(kind: ShapeKind, side: number) {
    super(kind);
    this.side = side;
  }

  override equals(other: any): boolean {
    return super.equals(other) && valueEquals(this.side, other.side);
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.side);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<Square>): Square {
    return copyRecordWith<Square>(this, overrides);
  }

  protected override $printMembers(): string {
    const base = super.$printMembers?.() ?? "";

    return `${base ? `${base}, ` : ""}Side = ${formatDouble(this.side)}`;
  }

  override toString(): string {
    return `Square { ${this.$printMembers()} }`;
  }
}

export function isSquare(value: unknown): value is Square {
  if (value instanceof Square) {
    return true;
  }

  if (value == null || typeof value !== "object") {
    return false;
  }

  const v = value as any;

  return typeof v.side === "number" && v.kind != null;
}

export function assertSquare(value: unknown, message?: string): asserts value is Square {
  if (!isSquare(value)) {
    throw new TypeError(message ?? "Value is not a Square");
  }
}
