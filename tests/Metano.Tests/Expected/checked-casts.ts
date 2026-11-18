// casts/circle.ts
import type { ClassType } from "metano-runtime";
import { IHasArea$runtimeType, type IHasArea } from "./i-has-area";
import { Shape } from "./shape";

let Circle$rt: ClassType<Circle> | undefined;

export class Circle extends Shape implements IHasArea {
  constructor() {
    super();
  }

  static override get $runtimeType(): ClassType<Circle> {
    return (Circle$rt ??= {
      kind: "class",
      name: "Circle",
      fullName: "Casts.Circle",
      namespace: "Casts",
      ctor: Circle,
      base: () => Shape.$runtimeType,
      interfaces: [
        () => IHasArea$runtimeType,
      ],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new Circle(),
      test: (v: unknown): v is Circle => v instanceof Circle,
    });
  }

  get area(): number {
    return 1;
  }
}

// casts/color.ts
import { isInt32, type EnumType } from "metano-runtime";

export const Color = {
  Red: 0,
  Green: 1,
} as const;

export type Color = typeof Color[keyof typeof Color];

export const Color$runtimeType: EnumType<Color> = {
  kind: "enum",
  name: "Color",
  fullName: "Casts.Color",
  namespace: "Casts",
  values: [0, 1],
  names: ["Red", "Green"],
  isFlags: false,
  test: (v: unknown): v is Color => isInt32(v),
};

// casts/conversions.ts
import { asType, castTo, DecimalType, Int32Type, Int64Type, StringType } from "metano-runtime";
import type { Decimal } from "metano-runtime/system/decimal";
import { Circle } from "./circle";
import { Color$runtimeType, type Color } from "./color";
import { IHasArea$runtimeType, type IHasArea } from "./i-has-area";
import { Meter } from "./meter";
import { Perms$runtimeType, type Perms } from "./perms";
import type { Shape } from "./shape";

export class Conversions {
  constructor() { }

  static toCircle(s: Shape): Circle {
    return castTo<Circle>(s, Circle);
  }

  static toArea(o: Object): IHasArea {
    return castTo<IHasArea>(o, IHasArea$runtimeType);
  }

  static toText(o: Object): string {
    return castTo<string>(o, StringType);
  }

  static toInt(o: Object): number {
    return castTo<number>(o, Int32Type);
  }

  static toLong(o: Object): bigint {
    return castTo<bigint>(o, Int64Type);
  }

  static toDecimal(o: Object): Decimal {
    return castTo<Decimal>(o, DecimalType);
  }

  static toColor(o: Object): Color {
    return castTo<Color>(o, Color$runtimeType);
  }

  static toPerms(o: Object): Perms {
    return castTo<Perms>(o, Perms$runtimeType);
  }

  static toMeter(o: Object): Meter {
    return castTo<Meter>(o, Meter);
  }

  static toObjects(o: Object): Object[] {
    return o as Object[];
  }

  static toInts(o: Object): number[] {
    return o as number[];
  }

  static toAction(o: Object): () => void {
    return o as () => void;
  }

  static toParameter<T>(o: Object): T {
    return o as T;
  }

  static asArea(o: Object): IHasArea | null {
    return asType(o, IHasArea$runtimeType);
  }

  static asInt(o: Object): number | null {
    return asType(o, Int32Type);
  }
}

// casts/i-has-area.ts
import { implementsInterface, type InterfaceType } from "metano-runtime";

export interface IHasArea {
  readonly area: number;
}

export const IHasArea$runtimeType: InterfaceType<IHasArea> = {
  kind: "interface",
  name: "IHasArea",
  fullName: "Casts.IHasArea",
  namespace: "Casts",
  baseInterfaces: [],
  plainImplementers: [],
  test: (v: unknown): v is IHasArea => implementsInterface(v, IHasArea$runtimeType),
};

// casts/index.ts
export { Circle } from "./circle";
export { Color, Color$runtimeType } from "./color";
export { Conversions } from "./conversions";
export { IHasArea$runtimeType } from "./i-has-area";
export type { IHasArea } from "./i-has-area";
export { Meter } from "./meter";
export { Perms, Perms$runtimeType } from "./perms";
export { Shape } from "./shape";

// casts/meter.ts
import { HashCode, valueEquals } from "metano-runtime";

export class Meter {
  value: number = 0;

  constructor() { }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && valueEquals(this.value, other.value);
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.value);

    return hc.toHashCode();
  }
}

// casts/perms.ts
import { isInt32, type EnumType } from "metano-runtime";

export const Perms = {
  None: 0,
  Read: 1,
  Write: 2,
} as const;

export type Perms = number & { readonly __enum?: "Perms" };

export const Perms$runtimeType: EnumType<Perms> = {
  kind: "enum",
  name: "Perms",
  fullName: "Casts.Perms",
  namespace: "Casts",
  values: [0, 1, 2],
  names: ["None", "Read", "Write"],
  isFlags: true,
  test: (v: unknown): v is Perms => isInt32(v),
};

// casts/shape.ts
import type { ClassType } from "metano-runtime";

let Shape$rt: ClassType<Shape> | undefined;

export class Shape {
  constructor() { }

  static get $runtimeType(): ClassType<Shape> {
    return (Shape$rt ??= {
      kind: "class",
      name: "Shape",
      fullName: "Casts.Shape",
      namespace: "Casts",
      ctor: Shape,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new Shape(),
      test: (v: unknown): v is Shape => v instanceof Shape,
    });
  }
}
