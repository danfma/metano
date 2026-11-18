/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import { UUID, isNullOrWhiteSpace } from "metano-runtime";
import { InvalidOperationException } from "metano-runtime/system/exceptions/invalid-operation-exception";

export type UserId = string & { readonly __brand: "UserId" };

function UserId$create(value: string): UserId {
  return value as UserId;
}

function UserId$new_(): UserId {
  return UserId.create(UUID.newUuid().replace(/-/g, ""));
}

function UserId$system(): UserId {
  return UserId.create("system");
}

function UserId$from(value: string): UserId {
  if (isNullOrWhiteSpace(value)) {
    throw new InvalidOperationException("A user id cannot be blank.");
  }

  return UserId.create(value.trim());
}

export const UserId = {
  create: UserId$create,
  new_: UserId$new_,
  system: UserId$system,
  from: UserId$from,
} as const;
