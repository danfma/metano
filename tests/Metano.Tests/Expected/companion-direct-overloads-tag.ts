import { isInt32, isInt64, isString } from "metano-runtime";
import { UId } from "./u-id";

export type Tag = string & { readonly __brand: "Tag" };

function Tag$create(value: string): Tag {
  return value as Tag;
}

function Tag$sL(l: bigint): string {
  return "long";
}

function Tag$sU(u: UId): string {
  return "uid";
}

function Tag$s(l: bigint): string;
function Tag$s(u: UId): string;
function Tag$s(...args: unknown[]): string {
  if (args.length === 1 && isInt64(args[0])) {
    return Tag.sL(args[0] as bigint);
  }

  if (args.length === 1 && typeof args[0] === "bigint") {
    return Tag.sU(args[0] as UId);
  }

  throw new Error("No matching overload for s");
}

function Tag$anyValue<T>(value: T): string {
  return "any";
}

function Tag$anyAB(a: number, b: number = 0): string {
  return "int";
}

function Tag$any<T>(value: T): string;
function Tag$any(a: number, b?: number): string;
function Tag$any(...args: unknown[]): any {
  if (args.length === 1) {
    return Tag.anyValue(args[0] as any);
  }

  if (args.length >= 1 && args.length <= 2 && isInt32(args[0]) && (args[1] === undefined || isInt32(args[1]))) {
    return Tag.anyAB(args[0] as number, args[1] as number);
  }

  throw new Error("No matching overload for any");
}

function Tag$nS(s: string): string {
  return "string";
}

function Tag$nI(i: number): string {
  return "int";
}

function Tag$n(s: string): string;
function Tag$n(i: number): string;
function Tag$n(...args: unknown[]): string {
  if (args.length === 1 && isString(args[0])) {
    return Tag.nS(args[0] as string);
  }

  if (args.length === 1 && isInt32(args[0])) {
    return Tag.nI(args[0] as number);
  }

  throw new Error("No matching overload for n");
}

function Tag$both(): string {
  return (Tag.sU(UId.create(2n)) ?? "") + (Tag.sL(2n) ?? "");
}

export const Tag = {
  create: Tag$create,
  sL: Tag$sL,
  sU: Tag$sU,
  s: Tag$s,
  anyValue: Tag$anyValue,
  anyAB: Tag$anyAB,
  any: Tag$any,
  nS: Tag$nS,
  nI: Tag$nI,
  n: Tag$n,
  both: Tag$both,
} as const;
