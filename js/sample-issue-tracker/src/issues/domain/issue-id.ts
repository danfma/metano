import { HashCode, UUID } from "metano-runtime";

export type IssueId = string & { readonly __brand: "IssueId" };

export namespace IssueId {
  export function create(value: string): IssueId {
    return value as IssueId;
  }

  export function new_(): IssueId {
    return IssueId.create(UUID.newUuid().replace(/-/g, ""));
  }
}
