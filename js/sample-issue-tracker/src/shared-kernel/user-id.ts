import { HashCode } from "@meta-sharp/runtime";

export type UserId = string & { readonly __brand: "UserId" };

export namespace UserId {
  export function create(value: string): UserId {
    return value as UserId;
  }

  export function new_(): UserId {
    return UserId.create(crypto.randomUUID().replace(/-/g, ""));
  }

  export function system(): UserId {
    return UserId.create("system");
  }
}
