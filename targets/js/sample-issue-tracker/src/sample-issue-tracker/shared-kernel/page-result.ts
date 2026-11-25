/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import {
  HashCode,
  copyRecordWith,
  formatBoolean,
  int32FromDouble,
  valueEquals,
  type ClassType,
} from "metano-runtime";
import type { IReadOnlyList } from "metano-runtime/system/collections";
import type { PageRequest } from "./page-request.js";

let PageResult$rt: ClassType<PageResult<unknown>> | undefined;

export class PageResult<T> {
  declare readonly items: IReadOnlyList<T>;
  declare readonly totalCount: number;
  declare readonly page: PageRequest;

  constructor(items: IReadOnlyList<T>, totalCount: number, page: PageRequest) {
    this.items = items;
    this.totalCount = totalCount;
    this.page = page;
  }

  static get $runtimeType(): ClassType<PageResult<unknown>> {
    return (PageResult$rt ??= {
      kind: "class",
      name: "PageResult`1",
      fullName: "SampleIssueTracker.SharedKernel.PageResult`1",
      namespace: "SampleIssueTracker.SharedKernel",
      ctor: PageResult,
      interfaces: [],
      typeParameters: ["T"],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is PageResult<unknown> => v instanceof PageResult,
    });
  }

  get totalPages(): number {
    return this.totalCount === 0
      ? 0
      : int32FromDouble(Math.ceil(this.totalCount / this.page.safeSize));
  }

  get hasNextPage(): boolean {
    return this.page.safeNumber < this.totalPages;
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.items === other.items &&
      this.totalCount === other.totalCount &&
      valueEquals(this.page, other.page)
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.items);
    hc.add(this.totalCount);
    hc.add(this.page);

    return hc.toHashCode();
  }

  with(overrides?: Partial<PageResult<T>>): PageResult<T> {
    return copyRecordWith<PageResult<T>>(this, overrides);
  }

  protected $printMembers(): string {
    return `Items = ${this.items ?? ""}, TotalCount = ${this.totalCount}, Page = ${this.page ?? ""}, TotalPages = ${this.totalPages}, HasNextPage = ${formatBoolean(this.hasNextPage)}`;
  }

  toString(): string {
    return `PageResult { ${this.$printMembers()} }`;
  }
}
