import { HashCode } from "@meta-sharp/runtime";
export class IssueId {
  constructor(readonly value: string) { }

  static new(): IssueId {
    return new IssueId(crypto.randomUUID().toString("N"));
  }

  toString(): string {
    return this.value;
  }

  equals(other: any): boolean {
    return other instanceof IssueId && this.value === other.value;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.value);
    return hc.toHashCode();
  }

  with(overrides?: Partial<IssueId>): IssueId {
    return new IssueId(overrides?.value ?? this.value);
  }
}
