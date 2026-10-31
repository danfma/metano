/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith } from "metano-runtime";

export class User {
  constructor(
    readonly name: string,
    readonly age: number,
    readonly active: boolean,
  ) {}

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.name === other.name &&
      this.age === other.age &&
      this.active === other.active
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.name);
    hc.add(this.age);
    hc.add(this.active);

    return hc.toHashCode();
  }

  with(overrides?: Partial<User>): User {
    return copyRecordWith<User>(this, overrides);
  }
}
