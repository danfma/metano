// zoo/badge.ts
import { HashCode, type ClassType } from "metano-runtime";
import { ITagged$runtimeType, type ITagged } from "./i-tagged";

let Badge$rt: ClassType<Badge> | undefined;

export class Badge implements ITagged {
  constructor() { }

  static get $runtimeType(): ClassType<Badge> {
    return (Badge$rt ??= {
      kind: "class",
      name: "Badge",
      fullName: "Zoo.Badge",
      namespace: "Zoo",
      ctor: Badge,
      interfaces: [
        () => ITagged$runtimeType,
      ],
      typeParameters: [],
      isAbstract: false,
      isValueType: true,
      default: () => new Badge(),
      create: () => new Badge(),
      test: (v: unknown): v is Badge => v instanceof Badge,
    });
  }

  get tag(): string {
    return "badge";
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this);
  }

  hashCode(): number {
    const hc = new HashCode();

    return hc.toHashCode();
  }
}

// zoo/dog.ts
import type { ClassType } from "metano-runtime";
import { INamed$runtimeType } from "./i-named";
import { IPet$runtimeType, type IPet } from "./i-pet";

let Dog$rt: ClassType<Dog> | undefined;

export class Dog implements IPet {
  constructor() { }

  static get $runtimeType(): ClassType<Dog> {
    return (Dog$rt ??= {
      kind: "class",
      name: "Dog",
      fullName: "Zoo.Dog",
      namespace: "Zoo",
      ctor: Dog,
      interfaces: [
        () => IPet$runtimeType,
        () => INamed$runtimeType,
      ],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new Dog(),
      test: (v: unknown): v is Dog => v instanceof Dog,
    });
  }

  get name(): string {
    return "dog";
  }

  get friendly(): boolean {
    return true;
  }
}

// zoo/i-named.ts
import { implementsInterface, type InterfaceType } from "metano-runtime";

export interface INamed {
  readonly name: string;
}

export const INamed$runtimeType: InterfaceType<INamed> = {
  kind: "interface",
  name: "INamed",
  fullName: "Zoo.INamed",
  namespace: "Zoo",
  baseInterfaces: [],
  plainImplementers: [],
  test: (v: unknown): v is INamed => implementsInterface(v, INamed$runtimeType),
};

// zoo/i-pet.ts
import { implementsInterface, type InterfaceType } from "metano-runtime";
import { INamed$runtimeType, type INamed } from "./i-named";

export interface IPet extends INamed {
  readonly friendly: boolean;
}

export const IPet$runtimeType: InterfaceType<IPet> = {
  kind: "interface",
  name: "IPet",
  fullName: "Zoo.IPet",
  namespace: "Zoo",
  baseInterfaces: [
    () => INamed$runtimeType,
  ],
  plainImplementers: [],
  test: (v: unknown): v is IPet => implementsInterface(v, IPet$runtimeType),
};

// zoo/i-tagged.ts
import { implementsInterface, type InterfaceType } from "metano-runtime";

export interface ITagged {
  readonly tag: string;
}

export const ITagged$runtimeType: InterfaceType<ITagged> = {
  kind: "interface",
  name: "ITagged",
  fullName: "Zoo.ITagged",
  namespace: "Zoo",
  baseInterfaces: [],
  plainImplementers: [],
  test: (v: unknown): v is ITagged => implementsInterface(v, ITagged$runtimeType),
};

export function isITagged(value: unknown): value is ITagged {
  if (value == null || typeof value !== "object") {
    return false;
  }

  const v = value as any;

  return typeof v.tag === "string";
}

export function assertITagged(value: unknown, message?: string): asserts value is ITagged {
  if (!isITagged(value)) {
    throw new TypeError(message ?? "Value is not a ITagged");
  }
}

// zoo/i-unasked.ts
export interface IUnasked {
  readonly x: number;
}

// zoo/index.ts
export { Badge } from "./badge";
export { Dog } from "./dog";
export { INamed$runtimeType } from "./i-named";
export type { INamed } from "./i-named";
export { IPet$runtimeType } from "./i-pet";
export type { IPet } from "./i-pet";
export { assertITagged, isITagged, ITagged$runtimeType } from "./i-tagged";
export type { ITagged } from "./i-tagged";
export type { IUnasked } from "./i-unasked";
export { Keeper } from "./keeper";
export { Puppy } from "./puppy";
export { Rock } from "./rock";

// zoo/keeper.ts
import { asType, castTo, isString, isType } from "metano-runtime";
import { INamed$runtimeType, type INamed } from "./i-named";
import { IPet$runtimeType, type IPet } from "./i-pet";
import { ITagged$runtimeType } from "./i-tagged";

export class Keeper {
  constructor() { }

  static isNamed(o: Object): boolean {
    return isType(o, INamed$runtimeType);
  }

  static isTagged(o: Object): boolean {
    return isType(o, ITagged$runtimeType);
  }

  static asPet(o: Object): IPet | null {
    return asType(o, IPet$runtimeType);
  }

  static named(o: Object): INamed {
    return castTo<INamed>(o, INamed$runtimeType);
  }

  static greetNamed(named: INamed): string {
    return named.name;
  }

  static greetText(text: string): string {
    return text;
  }

  static greet(named: INamed): string;
  static greet(text: string): string;
  static greet(...args: unknown[]): string {
    if (args.length === 1 && isType(args[0], INamed$runtimeType)) {
      return Keeper.greetNamed(args[0] as INamed);
    }

    if (args.length === 1 && isString(args[0])) {
      return Keeper.greetText(args[0] as string);
    }

    throw new Error("No matching overload for greet");
  }

  static kind(o: Object): string {
    return isType(o, IPet$runtimeType) ? "pet" : isType(o, INamed$runtimeType) ? "named" : "other";
  }
}

// zoo/puppy.ts
import type { ClassType } from "metano-runtime";
import { Dog } from "./dog";
import { INamed$runtimeType } from "./i-named";
import { IPet$runtimeType } from "./i-pet";

let Puppy$rt: ClassType<Puppy> | undefined;

export class Puppy extends Dog {
  constructor() {
    super();
  }

  static override get $runtimeType(): ClassType<Puppy> {
    return (Puppy$rt ??= {
      kind: "class",
      name: "Puppy",
      fullName: "Zoo.Puppy",
      namespace: "Zoo",
      ctor: Puppy,
      base: () => Dog.$runtimeType,
      interfaces: [
        () => IPet$runtimeType,
        () => INamed$runtimeType,
      ],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new Puppy(),
      test: (v: unknown): v is Puppy => v instanceof Puppy,
    });
  }
}

// zoo/rock.ts
import type { IUnasked } from "./i-unasked";

export class Rock implements IUnasked {
  constructor() { }

  get x(): number {
    return 1;
  }
}
