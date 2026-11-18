import { isInt64 } from "metano-runtime";
import type { UId } from "./u-id";

export type Ticks = bigint & { readonly __brand: "Ticks" };

function Ticks$create(value: bigint): Ticks {
  return value as Ticks;
}

function Ticks$toString(value: Ticks): string {
  return String(value);
}

function Ticks$__addTicksInt64(a: Ticks, b: bigint): Ticks {
  return Ticks.create(BigInt.asIntN(64, a + b));
}

function Ticks$__addTicksUId(a: Ticks, b: UId): Ticks {
  return Ticks.create(BigInt.asIntN(64, a + b));
}

function Ticks$__add(a: Ticks, b: bigint): Ticks;
function Ticks$__add(a: Ticks, b: UId): Ticks;
function Ticks$__add(...args: unknown[]): Ticks {
  if (args.length === 2 && isInt64(args[0]) && isInt64(args[1])) {
    return Ticks.__addTicksInt64(args[0] as Ticks, args[1] as bigint);
  }

  if (args.length === 2 && isInt64(args[0]) && isInt64(args[1])) {
    return Ticks.__addTicksUId(args[0] as Ticks, args[1] as UId);
  }

  throw new Error("No matching overload for add");
}

export const Ticks = {
  create: Ticks$create,
  toString: Ticks$toString,
  __addTicksInt64: Ticks$__addTicksInt64,
  __addTicksUId: Ticks$__addTicksUId,
  __add: Ticks$__add,
} as const;
