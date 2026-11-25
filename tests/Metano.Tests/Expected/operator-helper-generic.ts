import { isInt32, isInt64 } from "metano-runtime";

export class Box<T> {
  declare item: T;
  declare count: number;

  constructor(item: T, count: number) {
    this.item = item;
    this.count = count;
  }

  static __addBoxOfTInt32<T>(b: Box<T>, n: number): Box<T> {
    return new Box(b.item, (b.count + n) | 0);
  }

  static __addBoxOfTInt64<T>(b: Box<T>, n: bigint): Box<T> {
    return new Box(b.item, (b.count + Number(BigInt.asIntN(32, n))) | 0);
  }

  static __add<T>(b: Box<T>, n: number): Box<T>;
  static __add<T>(b: Box<T>, n: bigint): Box<T>;
  static __add<T>(...args: unknown[]): Box<T> {
    if (args.length === 2 && args[0] instanceof Box && isInt32(args[1])) {
      return Box.__addBoxOfTInt32(args[0] as Box<T>, args[1] as number);
    }

    if (args.length === 2 && args[0] instanceof Box && isInt64(args[1])) {
      return Box.__addBoxOfTInt64(args[0] as Box<T>, args[1] as bigint);
    }

    throw new Error("No matching overload for add");
  }

  $add(n: number): Box<T>;
  $add(n: bigint): Box<T>;
  $add(...args: unknown[]): Box<T> {
    if (args.length === 1 && isInt32(args[0])) {
      return Box.__addBoxOfTInt32(this, args[0] as number);
    }

    if (args.length === 1 && isInt64(args[0])) {
      return Box.__addBoxOfTInt64(this, args[0] as bigint);
    }

    throw new Error("No matching overload for add");
  }

  static __subtract<T>(b: Box<T>, n: number): Box<T> {
    return new Box(b.item, (b.count - n) | 0);
  }

  $subtract(n: number): Box<T> {
    return Box.__subtract(this, n);
  }

  static __implicitToT<T>(b: Box<T>): T {
    return b.item;
  }
}
