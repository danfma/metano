import { HashCode } from "metano-runtime";

export class Amount {
  constructor(readonly value: number) { }

  doubled(): number {
    return this.value * 2;
  }

  static fromValue(value: number): Amount {
    return new Amount(value);
  }

  equals(other: any): boolean {
    return other instanceof Amount && this.value === other.value;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.value);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Amount>): Amount {
    return new Amount(overrides?.value ?? this.value);
  }
}
