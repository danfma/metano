/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";
import { Clip } from "./clip";

export class Group extends Clip {
  constructor(
    readonly name: string,
    readonly members: Clip[],
  ) {
    super(0);
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.name === other.name && this.members === other.members;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.name);
    hc.add(this.members);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<Group>): Group {
    return copyRecordWith<Group>(this, overrides);
  }
}
