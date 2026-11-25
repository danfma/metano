// fallbacks/box.ts
import type { ClassType } from "metano-runtime";

let Box$rt: ClassType<Box<unknown>> | undefined;

export class Box<T> {
  declare readonly value: T;

  constructor(value: T) {
    this.value = value;
    this.value = value;
  }

  static get $runtimeType(): ClassType<Box<unknown>> {
    return (Box$rt ??= {
      kind: "class",
      name: "Box`1",
      fullName: "Fallbacks.Box`1",
      namespace: "Fallbacks",
      ctor: Box,
      interfaces: [],
      typeParameters: ["T"],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is Box<unknown> => v instanceof Box,
    });
  }
}

// fallbacks/i-shape.ts
import { implementsInterface, type InterfaceType } from "metano-runtime";

export interface IShape {
}

export const IShape$runtimeType: InterfaceType<IShape> = {
  kind: "interface",
  name: "IShape",
  fullName: "Fallbacks.IShape",
  namespace: "Fallbacks",
  baseInterfaces: [],
  plainImplementers: [],
  test: (v: unknown): v is IShape => implementsInterface(v, IShape$runtimeType),
};

// fallbacks/index.ts
export { Box } from "./box.js";
export { IShape$runtimeType } from "./i-shape.js";
export type { IShape } from "./i-shape.js";
export { Meter } from "./meter.js";
export { Mode, Mode$runtimeType } from "./mode.js";
export { Reflect } from "./reflect.js";
export { Token } from "./token.js";

// fallbacks/meter.ts
import { HashCode, valueEquals, type ClassType } from "metano-runtime";

let Meter$rt: ClassType<Meter> | undefined;

export class Meter {
  declare value: number;

  constructor() {
    this.value = 0;
  }

  static get $runtimeType(): ClassType<Meter> {
    return (Meter$rt ??= {
      kind: "class",
      name: "Meter",
      fullName: "Fallbacks.Meter",
      namespace: "Fallbacks",
      ctor: Meter,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: true,
      default: () => Meter.$default(),
      create: () => Meter.$default(),
      test: (v: unknown): v is Meter => v instanceof Meter,
    });
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && valueEquals(this.value, other.value);
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.value);

    return hc.toHashCode();
  }

  static $default(): Meter {
    return Object.assign(Object.create(Meter.prototype), { value: 0 });
  }
}

// fallbacks/mode.ts
import { isInt32, type EnumType } from "metano-runtime";

export const Mode = {
  A: 0,
  B: 1,
} as const;

export type Mode = typeof Mode[keyof typeof Mode];

export const Mode$runtimeType: EnumType<Mode> = {
  kind: "enum",
  name: "Mode",
  fullName: "Fallbacks.Mode",
  namespace: "Fallbacks",
  values: [0, 1],
  names: ["A", "B"],
  isFlags: false,
  default: () => 0,
  test: (v: unknown): v is Mode => isInt32(v),
};

// fallbacks/reflect.ts
import { ArrayType, DelegateType, Int32Type, ObjectType, StringType, formatBoolean, getRuntimeType, isAssignableFrom, isType, specialize, type RuntimeType } from "metano-runtime";
import { InvalidOperationException } from "metano-runtime/system/exceptions/invalid-operation-exception";
import { Box } from "./box.js";
import { IShape$runtimeType } from "./i-shape.js";
import { Meter } from "./meter.js";
import { Mode$runtimeType } from "./mode.js";
import type { Token } from "./token.js";

export class Reflect {
  private constructor() { }

  private static next(): number {
    return 1;
  }

  static ofValue(o: Object): string {
    return getRuntimeType(o).fullName ?? "";
  }

  static ofCall(): string {
    return getRuntimeType(Reflect.next()).name;
  }

  static ofSealed(token: Token): string {
    return getRuntimeType(token).name;
  }

  static isIntBox(o: Object): boolean {
    return getRuntimeType(o) === specialize(Box.$runtimeType, [Int32Type]);
  }

  static types(): RuntimeType[] {
    return [Int32Type, StringType, ArrayType, DelegateType, ObjectType, IShape$runtimeType, Mode$runtimeType, Meter.$runtimeType, InvalidOperationException.$runtimeType];
  }

  static surface(type: RuntimeType, o: Object): string {
    return `${type.name} ${type.fullName ?? ""} ${type.namespace ?? ""} ${formatBoolean(((type.kind as string) === "interface"))} ${formatBoolean(((type.kind as string) === "class"))} ` + `${formatBoolean(isType(o, type))} ${formatBoolean(isAssignableFrom(type, IShape$runtimeType))} ` + `${formatBoolean(type === Mode$runtimeType)} ${formatBoolean(type !== Mode$runtimeType)} ${formatBoolean((type === Meter.$runtimeType))} ` + type.fullName;
  }
}

// fallbacks/token.ts
import type { ClassType } from "metano-runtime";

let Token$rt: ClassType<Token> | undefined;

export class Token {
  static get $runtimeType(): ClassType<Token> {
    return (Token$rt ??= {
      kind: "class",
      name: "Token",
      fullName: "Fallbacks.Token",
      namespace: "Fallbacks",
      ctor: Token,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new Token(),
      test: (v: unknown): v is Token => v instanceof Token,
    });
  }
}
