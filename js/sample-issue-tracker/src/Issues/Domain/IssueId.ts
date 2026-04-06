import { HashCode } from "@meta-sharp/runtime";
export type IssueId = string & { readonly __brand: "IssueId" };
export const IssueId = {
  create: (value: string) => value as IssueId,
  new: () => IssueId.create(crypto.randomUUID().replace(/-/g, "")),
} as const;
