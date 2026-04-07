import { HashCode } from "@meta-sharp/runtime";

export type IssueId = string & { readonly __brand: "IssueId" };

export namespace IssueId {
  export function create(value: string): IssueId {
    return value as IssueId;
  }

  export function new_(): IssueId {
    return IssueId.create(crypto.randomUUID().replace(/-/g, ""));
  }
}
