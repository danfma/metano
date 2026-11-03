import { objectEquals, tupleEquals } from "metano-runtime";

export class Comparisons {
  constructor() { }

  static flat(a: [number, number], b: [number, number]): boolean {
    return tupleEquals(a, b);
  }

  static nested(a: [[number, number], number], b: [[number, number], number]): boolean {
    return tupleEquals(a, b, [[], null]);
  }

  static deeper(a: [[[number, number], number], number], b: [[[number, number], number], number]): boolean {
    return tupleEquals(a, b, [
      [[], null],
      null,
    ]);
  }

  static named(a: [[number, number], number], b: [[number, number], number]): boolean {
    return tupleEquals(a, b, [[], null]);
  }

  static mixed(a: [[number, number], number[]], b: [[number, number], number[]]): boolean {
    return tupleEquals(a, b, [[], null]);
  }

  static nullableBoth(a: [[number, number], number] | null, b: [[number, number], number] | null): boolean {
    return tupleEquals(a, b, [[], null]);
  }

  static guarded(a: [[number, number], number] | null, b: [[number, number], number]): boolean | null {
    return a == null ? null : tupleEquals(a, b, [[], null]);
  }

  static nullableArgument(a: [[number, number], number], b: [[number, number], number] | null): boolean {
    return tupleEquals(a, b, [[], null]);
  }

  static namedArgument(a: [[number, number], number], b: [[number, number], number]): boolean {
    return tupleEquals(a, b, [[], null]);
  }

  static reversedNamed(a: [[number, number], number], b: [[number, number], number]): boolean {
    return tupleEquals(a, b, [[], null]);
  }

  static throughValue(a: [[number, number], number] | null, b: [[number, number], number]): boolean {
    return tupleEquals(a, b, [[], null]);
  }

  static mismatched(a: [number, number], b: [number, number]): boolean {
    return objectEquals(a, b);
  }

  static nullableMismatched(a: [number, number] | null, b: [number, number]): boolean {
    return objectEquals(a, b);
  }

  static throughObject(a: [[number, number], number], b: Object): boolean {
    return tupleEquals(a, b);
  }

  static boxed(a: Object, b: Object): boolean {
    return objectEquals(a, b);
  }
}
