/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, formatBoolean, type ClassType } from "metano-runtime";

let User$rt: ClassType<User> | undefined;

export class User {
  declare readonly name: string;
  declare readonly age: number;
  declare readonly active: boolean;

  constructor(name: string, age: number, active: boolean) {
    this.name = name;
    this.age = age;
    this.active = active;
  }

  static get $runtimeType(): ClassType<User> {
    return (User$rt ??= {
      kind: "class",
      name: "User",
      fullName: "SampleQueryableArrays.User",
      namespace: "SampleQueryableArrays",
      ctor: User,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is User => v instanceof User,
    });
  }

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

  toString(): string {
    return `User { Name = ${this.name}, Age = ${this.age}, Active = ${formatBoolean(this.active)} }`;
  }
}
