import { isString } from "metano-runtime";

export type UserId = string & { readonly __brand: "UserId" };

function UserId$create(value: string): UserId {
  return value as UserId;
}

function UserId$fromRaw$(raw: string): string {
  return "string";
}

function UserId$fromId(id: UserId): string {
  return "userid";
}

function UserId$from(raw: string): string;
function UserId$from(id: UserId): string;
function UserId$from(...args: unknown[]): string {
  if (args.length === 1 && isString(args[0])) {
    return UserId.fromRaw$(args[0] as string);
  }

  if (args.length === 1 && typeof args[0] === "string") {
    return UserId.fromId(args[0] as UserId);
  }

  throw new Error("No matching overload for from");
}

function UserId$fromRaw(n: number): string {
  return "int";
}

export const UserId = {
  create: UserId$create,
  fromRaw$: UserId$fromRaw$,
  fromId: UserId$fromId,
  from: UserId$from,
  fromRaw: UserId$fromRaw,
} as const;
