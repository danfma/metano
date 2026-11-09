// base.ts
import { isInt32, isString } from "metano-runtime";

export class Base {
  constructor() { }

  addX(x: number): string {
    return "int";
  }

  addS(s: string): string {
    return "string";
  }

  add(x: number): string;
  add(s: string): string;
  add(...args: unknown[]): string {
    if (args.length === 1 && isInt32(args[0])) {
      return this.addX(args[0] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return this.addS(args[0] as string);
    }

    throw new Error("No matching overload for add");
  }
}

// circle.ts
import { isBool } from "metano-runtime";
import { Shape } from "./shape";

export class Circle extends Shape {
  constructor() {
    super();
  }

  override drawX(x: number): string {
    return "int";
  }

  override drawS(s: string): string {
    return "string";
  }

  drawB(b: boolean): string {
    return "bool";
  }

  override draw(x: number): string;
  override draw(s: string): string;
  override draw(b: boolean): string;
  override draw(...args: unknown[]): string {
    if (args.length === 1 && isBool(args[0])) {
      return this.drawB(args[0] as boolean);
    }

    return super.draw(...args as [never]);
  }
}

// clash.ts
import { isInt64 } from "metano-runtime";
import { Base } from "./base";

export class Clash extends Base {
  constructor() {
    super();
  }

  override addS(s: string): string {
    return "clash";
  }

  addInt64(x: number): string {
    return "long";
  }

  override add(x: number): string;
  override add(s: string): string;
  override add(x: number): string;
  override add(...args: unknown[]): string {
    if (args.length === 1 && isInt64(args[0])) {
      return this.addInt64(args[0] as number);
    }

    return super.add(...args as [never]);
  }
}

// derived.ts
import { isBool } from "metano-runtime";
import { Base } from "./base";

export class Derived extends Base {
  constructor() {
    super();
  }

  override addX(x: number): string {
    return "derived:" + super.addX(x);
  }

  addB(b: boolean): string {
    return "bool";
  }

  override add(x: number): string;
  override add(s: string): string;
  override add(b: boolean): string;
  override add(...args: unknown[]): string {
    if (args.length === 1 && isBool(args[0])) {
      return this.addB(args[0] as boolean);
    }

    return super.add(...args as [never]);
  }
}

// flag-mapper.ts
import { isBool } from "metano-runtime";
import { Mapper } from "./mapper";

export class FlagMapper extends Mapper {
  constructor() {
    super();
  }

  mapBC(b: boolean, c: boolean): string {
    return "flags";
  }

  override mapX<T>(x: T): string {
    return "flag:" + super.mapX<T>(x);
  }

  override map(x: number, y: number): string;
  override map<T>(x: T): string;
  override map(b: boolean, c: boolean): string;
  override map(...args: unknown[]): any {
    if (args.length === 2 && isBool(args[0]) && isBool(args[1])) {
      return this.mapBC(args[0] as boolean, args[1] as boolean);
    }

    return super.map(...args as [never]);
  }
}

// i-sink.ts
export interface ISink {
  write(x: number): string;
  write(s: string): string;
  writeX(x: number): string;
  writeS(s: string): string;
}

// index.ts
export { Base } from "./base";
export { Circle } from "./circle";
export { Clash } from "./clash";
export { Derived } from "./derived";
export { FlagMapper } from "./flag-mapper";
export type { ISink } from "./i-sink";
export { Leaf } from "./leaf";
export { Mapper } from "./mapper";
export { Middle } from "./middle";
export { RenamedClash } from "./renamed-clash";
export { Renamed } from "./renamed";
export { Repo } from "./repo";
export { Shape } from "./shape";
export { Sink } from "./sink";
export { StrRepo } from "./str-repo";

// leaf.ts
import { isBool } from "metano-runtime";
import { Middle } from "./middle";

export class Leaf extends Middle {
  constructor() {
    super();
  }

  override addX(x: number): string {
    return "leaf";
  }

  override addS(s: string): string {
    return "leaf:" + super.addS(s);
  }

  override addD(d: number): string {
    return "leaf:" + super.addD(d);
  }

  addB(b: boolean): string {
    return "bool";
  }

  override add(x: number): string;
  override add(s: string): string;
  override add(d: number): string;
  override add(b: boolean): string;
  override add(...args: unknown[]): string {
    if (args.length === 1 && isBool(args[0])) {
      return this.addB(args[0] as boolean);
    }

    return super.add(...args as [never]);
  }
}

// mapper.ts
import { isInt32 } from "metano-runtime";

export class Mapper {
  constructor() { }

  mapXY(x: number, y: number): string {
    return "pair";
  }

  mapX<T>(x: T): string {
    return "generic";
  }

  map(x: number, y: number): string;
  map<T>(x: T): string;
  map(...args: unknown[]): any {
    if (args.length === 2 && isInt32(args[0]) && isInt32(args[1])) {
      return this.mapXY(args[0] as number, args[1] as number);
    }

    if (args.length === 1) {
      return this.mapX(args[0] as any);
    }

    throw new Error("No matching overload for map");
  }
}

// middle.ts
import { isFloat64 } from "metano-runtime";
import { Base } from "./base";

export class Middle extends Base {
  constructor() {
    super();
  }

  override addS(s: string): string {
    return "middle";
  }

  addD(d: number): string {
    return "double";
  }

  override add(x: number): string;
  override add(s: string): string;
  override add(d: number): string;
  override add(...args: unknown[]): string {
    if (args.length === 1 && isFloat64(args[0])) {
      return this.addD(args[0] as number);
    }

    return super.add(...args as [never]);
  }
}

// renamed-clash.ts
import { isBool } from "metano-runtime";
import { Base } from "./base";

export class RenamedClash extends Base {
  constructor() {
    super();
  }

  override addX(y: number): string {
    return "renamed";
  }

  addBoolean(x: boolean): string {
    return "bool";
  }

  override add(x: number): string;
  override add(s: string): string;
  override add(x: boolean): string;
  override add(...args: unknown[]): string {
    if (args.length === 1 && isBool(args[0])) {
      return this.addBoolean(args[0] as boolean);
    }

    return super.add(...args as [never]);
  }
}

// renamed.ts
import { isBool } from "metano-runtime";
import { Base } from "./base";

export class Renamed extends Base {
  constructor() {
    super();
  }

  override addX(y: number): string {
    return "renamed";
  }

  addY(y: boolean): string {
    return "bool";
  }

  override add(x: number): string;
  override add(s: string): string;
  override add(y: boolean): string;
  override add(...args: unknown[]): string {
    if (args.length === 1 && isBool(args[0])) {
      return this.addY(args[0] as boolean);
    }

    return super.add(...args as [never]);
  }
}

// repo.ts
import { isInt32 } from "metano-runtime";

export class Repo<T> {
  constructor() { }

  putCount(count: number): string {
    return "count";
  }

  putItem(item: T): string {
    return "item";
  }

  put(count: number): string;
  put(item: T): string;
  put(...args: unknown[]): string {
    if (args.length === 1 && isInt32(args[0])) {
      return this.putCount(args[0] as number);
    }

    if (args.length === 1) {
      return this.putItem(args[0] as T);
    }

    throw new Error("No matching overload for put");
  }
}

// shape.ts
import { isInt32, isString } from "metano-runtime";

export abstract class Shape {
  constructor() { }

  abstract drawX(x: number): string;

  abstract drawS(s: string): string;

  draw(x: number): string;
  draw(s: string): string;
  draw(...args: unknown[]): string {
    if (args.length === 1 && isInt32(args[0])) {
      return this.drawX(args[0] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return this.drawS(args[0] as string);
    }

    throw new Error("No matching overload for draw");
  }
}

// sink.ts
import { isBool, isInt32, isString } from "metano-runtime";
import type { ISink } from "./i-sink";

export class Sink implements ISink {
  constructor() { }

  writeX(x: number): string {
    return "int";
  }

  writeS(s: string): string {
    return "string";
  }

  writeB(b: boolean): string {
    return "bool";
  }

  write(x: number): string;
  write(s: string): string;
  write(b: boolean): string;
  write(...args: unknown[]): string {
    if (args.length === 1 && isInt32(args[0])) {
      return this.writeX(args[0] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return this.writeS(args[0] as string);
    }

    if (args.length === 1 && isBool(args[0])) {
      return this.writeB(args[0] as boolean);
    }

    throw new Error("No matching overload for write");
  }
}

// str-repo.ts
import { isBool } from "metano-runtime";
import { Repo } from "./repo";

export class StrRepo extends Repo<string> {
  constructor() {
    super();
  }

  override putItem(item: string): string {
    return "str:" + item;
  }

  putFlag(flag: boolean): string {
    return "flag";
  }

  override put(item: string): string;
  override put(count: number): string;
  override put(flag: boolean): string;
  override put(...args: unknown[]): string {
    if (args.length === 1 && isBool(args[0])) {
      return this.putFlag(args[0] as boolean);
    }

    return super.put(...args as [never]);
  }
}
