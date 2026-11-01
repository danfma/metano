import { HashCode, copyRecordWith } from "metano-runtime";
import { Entry } from "./entry";

export class Note extends Entry {
  constructor(readonly id: number, readonly text: string) {
    super();
  }

  nextId(): number {
    return this.id + 1;
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
}
