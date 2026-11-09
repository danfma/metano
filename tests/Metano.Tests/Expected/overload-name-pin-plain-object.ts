import { isInt32, isString } from "metano-runtime";

export interface Reading {
  readonly value: number;
}

export function getNum(self: Reading, x: number): number {
  return x;
}

import { getNum, type Reading } from "./reading";

export class Use {
  constructor() { }

  run(r: Reading): number {
    return getNum(r, 1);
  }
}
