import { HashCode } from "@meta-sharp/runtime";
import type { PageRequest } from "./page-request";
export class PageResult<T> {
  constructor(readonly items: T[], readonly totalCount: number, readonly page: PageRequest) { }

  get totalPages(): number {
    return this.totalCount === 0 ? 0 : Math.ceil(this.totalCount / this.page.safeSize);
  }

  get hasNextPage(): boolean {
    return this.page.safeNumber < this.totalPages;
  }

  equals(other: any): boolean {
    return other instanceof PageResult && this.items === other.items && this.totalCount === other.totalCount && this.page === other.page;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.items);
    hc.add(this.totalCount);
    hc.add(this.page);
    return hc.toHashCode();
  }

  with(overrides?: Partial<PageResult<T>>): PageResult<T> {
    return new PageResult(overrides?.items ?? this.items, overrides?.totalCount ?? this.totalCount, overrides?.page ?? this.page);
  }
}
