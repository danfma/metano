import type { IReadOnlyList } from "metano-runtime/system/collections";

export interface IRepository<T> {
  readonly items: IReadOnlyList<T>;
}
