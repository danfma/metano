import { HashCode } from "metano-runtime";

export class PageRequest {
  constructor(readonly number: number = 1, readonly size: number = 20) { }

  get safeNumber(): number {
    return Math.max(1, this.number);
  }

  get safeSize(): number {
    return Math.max(1, this.size);
  }

  get skip(): number {
    return (this.safeNumber - 1) * this.safeSize;
  }

  equals(other: any): boolean {
    return other instanceof PageRequest && this.number === other.number && this.size === other.size;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.number);
    hc.add(this.size);

    return hc.toHashCode();
  }

  with(overrides?: Partial<PageRequest>): PageRequest {
    return new PageRequest(overrides?.number ?? this.number, overrides?.size ?? this.size);
  }
}
