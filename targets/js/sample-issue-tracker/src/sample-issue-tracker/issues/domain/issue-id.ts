/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import { UUID } from "metano-runtime";

export type IssueId = string & { readonly __brand: "IssueId" };

export namespace IssueId {
  export function create(value: string): IssueId {
    return value as IssueId;
  }

  export function new_(): IssueId {
    return IssueId.create(UUID.newUuid().replace(/-/g, ""));
  }
}
