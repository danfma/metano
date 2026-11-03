/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { UUID, isNullOrWhiteSpace } from "metano-runtime";

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

  export function from(value: string): UserId {
    if (isNullOrWhiteSpace(value)) {
      throw new Error("A user id cannot be blank.");
    }

    return UserId.create(value.trim());
  }
}
