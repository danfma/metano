import { HashCode } from "@meta-sharp/runtime";
export class UserId {
  constructor(readonly value: string) { }

  static new(): UserId {
    return new UserId(crypto.randomUUID().toString("N"));
  }

  static system(): UserId {
    return new UserId("system");
  }

  toString(): string {
    return this.value;
  }

  equals(other: any): boolean {
    return other instanceof UserId && this.value === other.value;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.value);
    return hc.toHashCode();
  }

  with(overrides?: Partial<UserId>): UserId {
    return new UserId(overrides?.value ?? this.value);
  }
}
