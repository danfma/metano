import { HashCode } from "@meta-sharp/runtime";
export type UserId = string & { readonly __brand: "UserId" };
export const UserId = {
  create: (value: string) => value as UserId,
  new: () => UserId.create(crypto.randomUUID().replace(/-/g, "")),
  system: () => UserId.create("system"),
} as const;
