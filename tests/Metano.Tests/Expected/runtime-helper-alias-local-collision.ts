import { intDiv as intDiv$rt } from "metano-runtime";

export class Divider {
  static div(intDiv: number, b: number): number {
    return intDiv$rt((intDiv + b) | 0, 2);
  }
}
