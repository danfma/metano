import { HashCode } from "metano-runtime";
import { UUID } from "metano-runtime";

export type UserId = string & { readonly __brand: "UserId" };

export namespace UserId {
  export function create(value: string): UserId {
    return value as UserId;
  }

  export function new_(): UserId {
    return UserId.create(UUID.newUuid().replace(/-/g, ""));
  }

  export function system(): UserId {
    return UserId.create("system");
  }
}
