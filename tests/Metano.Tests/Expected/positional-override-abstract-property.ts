import { HashCode, copyRecordWith } from "metano-runtime";
import { Entry } from "./entry.js";

export class Note extends Entry {
  declare readonly id: number;
  declare readonly text: string;

  constructor(id: number, text: string) {
    super();
    this.id = id;
    this.text = text;
  }

  nextId(): number {
    return (this.id + 1) | 0;
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.id === other.id && this.text === other.text;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.id);
    hc.add(this.text);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<Note>): Note {
    return copyRecordWith<Note>(this, overrides);
  }

  protected override $printMembers(): string {
    const base = super.$printMembers?.() ?? "";

    return `${base ? `${base}, ` : ""}Text = ${this.text ?? ""}`;
  }

  override toString(): string {
    return `Note { ${this.$printMembers()} }`;
  }
}
