import { isInt32, isString } from "metano-runtime";

export type Meters = number & { readonly __brand: "Meters" };

function Meters$create(value: number): Meters {
  return value as Meters;
}

function Meters$toString(value: Meters): string {
  return String(value);
}

function Meters$of_A(a: number = 5): Meters {
  return Meters.create(a);
}

function Meters$of_SRest(s: string, ...rest: number[]): Meters {
  return Meters.create(rest.length);
}

function Meters$of_(a?: number): Meters;
function Meters$of_(s: string, ...rest: number[]): Meters;
function Meters$of_(...args: unknown[]): Meters {
  if (args.length <= 1 && (args[0] === undefined || isInt32(args[0]))) {
    return Meters.of_A(args[0] as number);
  }

  if (args.length >= 1 && isString(args[0]) && args.slice(1).every((value) => isInt32(value))) {
    return Meters.of_SRest(args[0] as string, ...args.slice(1) as number[]);
  }

  throw new Error("No matching overload for of");
}

export const Meters = {
  create: Meters$create,
  toString: Meters$toString,
  of_A: Meters$of_A,
  of_SRest: Meters$of_SRest,
  of_: Meters$of_,
} as const;
