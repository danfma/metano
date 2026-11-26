import { HashCode, copyRecordWith } from "metano-runtime";

abstract class ShotSource$Base {
  abstract readonly kind: "Fresh" | "Reused";

  static get Fresh(): typeof ShotSource$Fresh {
    return ShotSource$Fresh;
  }

  static get Reused(): typeof ShotSource$Reused {
    return ShotSource$Reused;
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this);
  }

  hashCode(): number {
    const hc = new HashCode();

    return hc.toHashCode();
  }

  with(overrides?: Partial<ShotSource>): ShotSource {
    return copyRecordWith<ShotSource>(this as unknown as ShotSource, overrides);
  }

  protected $printMembers(): string {
    return "";
  }

  toString(): string {
    const members = this.$printMembers();

    return members ? `ShotSource { ${members} }` : "ShotSource { }";
  }
}

export declare namespace ShotSource {
  export type Fresh = ShotSource$Fresh;

  export type Reused = ShotSource$Reused;
}

export type ShotSource = ShotSource$Fresh | ShotSource$Reused;

export const ShotSource = ShotSource$Base as { [Symbol.hasInstance]: <$P>(this: { prototype: $P }, value: unknown) => value is ShotSource$Base extends $P ? ShotSource : $P } & typeof ShotSource$Base;

class ShotSource$Fresh extends ShotSource {
  constructor() {
    super();
  }

  override get kind(): "Fresh" {
    return "Fresh";
  }

  override equals(other: any): boolean {
    return super.equals(other);
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());

    return hc.toHashCode();
  }

  override with(overrides?: Partial<ShotSource$Fresh>): ShotSource$Fresh {
    return copyRecordWith<ShotSource$Fresh>(this, overrides);
  }

  protected override $printMembers(): string {
    return super.$printMembers?.() ?? "";
  }

  override toString(): string {
    const members = this.$printMembers();

    return members ? `Fresh { ${members} }` : "Fresh { }";
  }
}

class ShotSource$Reused extends ShotSource {
  declare readonly from: number;

  constructor(from: number) {
    super();
    this.from = from;
  }

  override get kind(): "Reused" {
    return "Reused";
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.from === other.from;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.from);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<ShotSource$Reused>): ShotSource$Reused {
    return copyRecordWith<ShotSource$Reused>(this, overrides);
  }

  protected override $printMembers(): string {
    const base = super.$printMembers?.() ?? "";

    return `${base ? `${base}, ` : ""}From = ${this.from}`;
  }

  override toString(): string {
    return `Reused { ${this.$printMembers()} }`;
  }
}
