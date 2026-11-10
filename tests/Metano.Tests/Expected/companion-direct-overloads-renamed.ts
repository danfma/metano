import { isString } from "metano-runtime";

export type UserId = string & { readonly __brand: "UserId" };

export namespace UserId {
  export function create(value: string): UserId {
    return value as UserId;
  }

  export function fromRaw$(raw: string): string {
    return "string";
  }

  export function fromId(id: UserId): string {
    return "userid";
  }

  export function from(raw: string): string;
  export function from(id: UserId): string;
  export function from(...args: unknown[]): string {
    if (args.length === 1 && isString(args[0])) {
      return fromRaw$(args[0] as string);
    }

    if (args.length === 1 && typeof args[0] === "string") {
      return fromId(args[0] as UserId);
    }

    throw new Error("No matching overload for from");
  }

  export function fromRaw(n: number): string {
    return "int";
  }
}
