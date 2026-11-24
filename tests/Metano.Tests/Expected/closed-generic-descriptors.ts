// boxes/box.ts
import type { ClassType } from "metano-runtime";

let Box$rt: ClassType<Box<unknown>> | undefined;

export class Box<T> {
  value: T | null = null;

  constructor() { }

  static get $runtimeType(): ClassType<Box<unknown>> {
    return (Box$rt ??= {
      kind: "class",
      name: "Box`1",
      fullName: "Boxes.Box`1",
      namespace: "Boxes",
      ctor: Box,
      interfaces: [],
      typeParameters: ["T"],
      isAbstract: false,
      isValueType: false,
      create: () => new Box(),
      test: (v: unknown): v is Box<unknown> => v instanceof Box,
    });
  }
}

// boxes/cell.ts
import { HashCode, defaultEquals, type ClassType } from "metano-runtime";

let Cell$rt: ClassType<Cell<unknown>> | undefined;

export class Cell<T> {
  readonly value: T;

  constructor() { }

  static get $runtimeType(): ClassType<Cell<unknown>> {
    return (Cell$rt ??= {
      kind: "class",
      name: "Cell`1",
      fullName: "Boxes.Cell`1",
      namespace: "Boxes",
      ctor: Cell,
      interfaces: [],
      typeParameters: ["T"],
      isAbstract: false,
      isValueType: true,
      default: () => Cell.$default(),
      create: () => Cell.$default(),
      test: (v: unknown): v is Cell<unknown> => v instanceof Cell,
    });
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && defaultEquals(this.value, other.value);
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.value);

    return hc.toHashCode();
  }

  static $default<T>(): Cell<T> {
    return Object.assign(Object.create(Cell.prototype), { value: null });
  }
}

// boxes/gift.ts
import type { ClassType } from "metano-runtime";

let Gift$rt: ClassType<Gift> | undefined;

export class Gift {
  constructor() { }

  static get $runtimeType(): ClassType<Gift> {
    return (Gift$rt ??= {
      kind: "class",
      name: "Gift",
      fullName: "Boxes.Gift",
      namespace: "Boxes",
      ctor: Gift,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new Gift(),
      test: (v: unknown): v is Gift => v instanceof Gift,
    });
  }
}

// boxes/index.ts
export { Box } from "./box.js";
export { Cell } from "./cell.js";
export { Gift } from "./gift.js";
export { Pair } from "./pair.js";
export { Probe } from "./probe.js";

// boxes/pair.ts
import type { ClassType } from "metano-runtime";

let Pair$rt: ClassType<Pair<unknown, unknown>> | undefined;

export class Pair<TFirst, TSecond> {
  first: TFirst | null = null;
  second: TSecond | null = null;

  constructor() { }

  static get $runtimeType(): ClassType<Pair<unknown, unknown>> {
    return (Pair$rt ??= {
      kind: "class",
      name: "Pair`2",
      fullName: "Boxes.Pair`2",
      namespace: "Boxes",
      ctor: Pair,
      interfaces: [],
      typeParameters: ["TFirst", "TSecond"],
      isAbstract: false,
      isValueType: false,
      create: () => new Pair(),
      test: (v: unknown): v is Pair<unknown, unknown> => v instanceof Pair,
    });
  }
}

// boxes/probe.ts
import { DecimalType, Int32Type, StringType, castTo, getRuntimeType, specialize, type RuntimeType } from "metano-runtime";
import { Box } from "./box.js";
import { Cell } from "./cell.js";
import { Gift } from "./gift.js";
import { Pair } from "./pair.js";

export class Probe {
  constructor() { }

  static ofInt(): RuntimeType {
    return specialize(Box.$runtimeType, [Int32Type]);
  }

  static ofNested(): RuntimeType {
    return specialize(Box.$runtimeType, [specialize(Box.$runtimeType, [StringType])]);
  }

  static ofPair(): RuntimeType {
    return specialize(Pair.$runtimeType, [Gift.$runtimeType, DecimalType]);
  }

  static nameOf(): string {
    return specialize(Box.$runtimeType, [Int32Type]).name;
  }

  static ofCell(c: Cell<number>): RuntimeType {
    return specialize(Cell.$runtimeType, [Int32Type]);
  }

  static ofOpenCell<T>(c: Cell<T>): RuntimeType {
    return getRuntimeType(c);
  }

  static toBox(o: Object): Box<number> {
    return castTo<Box<number>>(o, Box);
  }

  static asBox(o: Object): Box<number> | null {
    return o instanceof Box ? o : null;
  }
}
