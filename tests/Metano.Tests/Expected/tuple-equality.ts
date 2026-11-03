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

  static nullableElement(a: [[number, number] | null, number], b: [[number, number] | null, number]): boolean {
    return tupleEquals(a, b, [[], null]);
  }

  static nullableReceiver(a: [number, number] | null, b: [number, number] | null): boolean {
    return tupleEquals(a, b);
  }

  static guarded(a: [[number, number], number] | null, b: [[number, number], number]): boolean | null {
    return a == null ? null : tupleEquals(a, b, [[], null]);
  }

  static conditionalMismatched(a: [[number, number], number] | null, b: [number, number]): boolean | null {
    return a == null ? null : false;
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
    return false;
  }

  static valueMismatched(a: [number, number], b: [number, number]): boolean {
    return false;
  }

  static nullableMismatchedObject(a: [number, number] | null, b: Object): boolean {
    return tupleEquals(a, b);
  }

  static boxedObject(a: [[number, number], number], b: Object): boolean {
    return tupleEquals(a, b, [[], null]);
  }

  static guardedObject(a: [[number, number], number] | null, b: Object): boolean | null {
    return a == null ? null : tupleEquals(a, b, [[], null]);
  }

  static valueTypeArgument(a: [[number, number], number], b: ValueType): boolean {
    return tupleEquals(a, b, [[], null]);
  }

  static interfaceArgument(a: [[number, number], number], b: IEquatable<[[number, number], number]>): boolean {
    return tupleEquals(a, b, [[], null]);
  }

  static typeParameterArgument<T>(a: [[number, number], number], b: T): boolean {
    return tupleEquals(a, b, [[], null]);
  }

  static throughObject(a: [[number, number], number], b: Object): boolean {
    return tupleEquals(a, b, [[], null]);
  }

  static boxed(a: Object, b: Object): boolean {
    return objectEquals(a, b);
  }

  static eight(a: [number, number, number, number, number, number, number, [number]], b: [number, number, number, number, number, number, number, [number]]): boolean {
    return tupleEquals(a, b, [null, null, null, null, null, null, null, []]);
  }
}
