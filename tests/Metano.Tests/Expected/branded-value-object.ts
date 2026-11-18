import { isInt32, isString } from "metano-runtime";
import { ArgumentException } from "metano-runtime/system/exceptions/argument-exception";

export type EpisodeSlug = string & { readonly __brand: "EpisodeSlug" };

function EpisodeSlug$create(value: string): EpisodeSlug {
  return value as EpisodeSlug;
}

function EpisodeSlug$fromValue(value: string): EpisodeSlug {
  if (value.trim().length === 0) {
    throw new ArgumentException("empty");
  }

  return EpisodeSlug.create(value.trim());
}

function EpisodeSlug$fromNumber(number: number): EpisodeSlug {
  return EpisodeSlug.create("ep-" + number);
}

function EpisodeSlug$from(value: string): EpisodeSlug;
function EpisodeSlug$from(number: number): EpisodeSlug;
function EpisodeSlug$from(...args: unknown[]): EpisodeSlug {
  if (args.length === 1 && isString(args[0])) {
    return EpisodeSlug.fromValue(args[0] as string);
  }

  if (args.length === 1 && isInt32(args[0])) {
    return EpisodeSlug.fromNumber(args[0] as number);
  }

  throw new Error("No matching overload for from");
}

function EpisodeSlug$__implicitToString(self: EpisodeSlug): string {
  return self;
}

function EpisodeSlug$__fromString(raw: string): EpisodeSlug {
  return EpisodeSlug.fromValue(raw);
}

export const EpisodeSlug = {
  create: EpisodeSlug$create,
  fromValue: EpisodeSlug$fromValue,
  fromNumber: EpisodeSlug$fromNumber,
  from: EpisodeSlug$from,
  __implicitToString: EpisodeSlug$__implicitToString,
  __fromString: EpisodeSlug$__fromString,
} as const;
