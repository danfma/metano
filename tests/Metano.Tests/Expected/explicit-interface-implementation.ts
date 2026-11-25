import type { ClassType } from "metano-runtime";
import type { IBox } from "./i-box.js";
import { IPlain$runtimeType, type IPlain } from "./i-plain.js";

let Person$rt: ClassType<Person> | undefined;

export class Person implements IPlain, IBox<string> {
  static get $runtimeType(): ClassType<Person> {
    return (Person$rt ??= {
      kind: "class",
      name: "Person",
      fullName: "Person",
      namespace: "",
      ctor: Person,
      interfaces: [
        () => IPlain$runtimeType,
      ],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new Person(),
      test: (v: unknown): v is Person => v instanceof Person,
    });
  }

  get name(): string {
    return "x";
  }

  get value(): string {
    return "v";
  }

  calc(x: number): number {
    return (x + 1) | 0;
  }

  shout(): string {
    return "X";
  }

  get(): string {
    return "g";
  }

  describe(other: IPlain): string {
    return (other.name ?? "") + this.calc(2) + (other.shout() ?? "");
  }
}
