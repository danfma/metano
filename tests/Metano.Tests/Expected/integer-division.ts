import { intDiv } from "metano-runtime";

export class Quotients {
  constructor() { }

  static average(total: number, count: number): number {
    return intDiv(total, count);
  }

  static halve(value: number): number {
    return intDiv(value, 2);
  }

  static split(value: number, parts: number): number {
    return intDiv(value, parts);
  }

  static bytes(a: number, b: number): number {
    return intDiv(a, b);
  }

  static lifted(a: number | null, b: number): number | null {
    return intDiv(a, b);
  }

  static mixed(a: number, b: number): number {
    return intDiv(a, b) * 2 + intDiv(a + 1, b);
  }

  static shrink(value: number): number {
    value = intDiv(value, 3);

    return value;
  }

  static ratio(a: number, b: number): number {
    return a / b;
  }

  static real(a: number, b: number): number {
    return a / b;
  }
}
