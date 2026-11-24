import { isInt64, isString } from "metano-runtime";

export type Gen<T> = bigint & { readonly __brand: "Gen" };

function Gen$create<T>(value: bigint): Gen<T> {
  return value as Gen<T>;
}

function Gen$toString<T>(value: Gen<T>): string {
  return String(value);
}

function Gen$ofTTL<T>(t: T, l: bigint): string {
  return "long";
}

function Gen$ofTTS<T>(t: T, s: string): string {
  return "string";
}

function Gen$ofT<T>(t: T, l: bigint): string;
function Gen$ofT<T>(t: T, s: string): string;
function Gen$ofT<T>(...args: unknown[]): string {
  if (args.length === 2 && isInt64(args[1])) {
    return Gen.ofTTL(args[0] as T, args[1] as bigint);
  }

  if (args.length === 2 && isString(args[1])) {
    return Gen.ofTTS(args[0] as T, args[1] as string);
  }

  throw new Error("No matching overload for ofT");
}

export const Gen = {
  create: Gen$create,
  toString: Gen$toString,
  ofTTL: Gen$ofTTL,
  ofTTS: Gen$ofTTS,
  ofT: Gen$ofT,
} as const;
