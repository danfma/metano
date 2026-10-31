import type { IBox } from "./i-box";
import type { IPlain } from "./i-plain";

export class Person implements IPlain, IBox<string> {
  constructor() { }

  get name(): string {
    return "x";
  }

  get value(): string {
    return "v";
  }

  calc(x: number): number {
    return x + 1;
  }

  shout(): string {
    return "X";
  }

  get(): string {
    return "g";
  }

  describe(other: IPlain): string {
    return other.name + this.calc(2) + other.shout();
  }
}
