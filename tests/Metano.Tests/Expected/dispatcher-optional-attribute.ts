import { isInt32, isString } from "metano-runtime";

export class Tagger {
  constructor() { }

  private tagCountExtra(count: number, extra: number): string {
    return "pair";
  }

  private tagLabel(label?: string | null): string {
    return label ?? "none";
  }

  tag(count: number, extra: number): string;
  tag(label?: string | null): string;
  tag(...args: unknown[]): string {
    if (args.length === 2 && isInt32(args[0]) && isInt32(args[1])) {
      return this.tagCountExtra(args[0] as number, args[1] as number);
    }

    if (args.length <= 1 && (args[0] === undefined || args[0] === null || isString(args[0]))) {
      return this.tagLabel(args[0] as string | null);
    }

    throw new Error("No matching overload for tag");
  }
}
