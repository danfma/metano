import { isInt32, isString } from "metano-runtime";

export type EpisodeSlug = string & { readonly __brand: "EpisodeSlug" };

export namespace EpisodeSlug {
  export function create(value: string): EpisodeSlug {
    return value as EpisodeSlug;
  }

  function fromValue(value: string): EpisodeSlug {
    if (value.trim().length === 0) {
      throw new Error("empty");
    }

    return EpisodeSlug.create(value.trim());
  }

  function fromNumber(number: number): EpisodeSlug {
    return EpisodeSlug.create("ep-" + number);
  }

  export function from(value: string): EpisodeSlug;
  export function from(number: number): EpisodeSlug;
  export function from(...args: unknown[]): EpisodeSlug {
    if (args.length === 1 && isString(args[0])) {
      return fromValue(args[0] as string);
    }

    if (args.length === 1 && isInt32(args[0])) {
      return fromNumber(args[0] as number);
    }

    throw new Error("No matching overload for from");
  }

  export function op_Explicit(raw: string): EpisodeSlug {
    return EpisodeSlug.from(raw);
  }
}
