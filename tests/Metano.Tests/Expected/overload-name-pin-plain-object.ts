export interface Reading {
  readonly value: number;
}

export function getNum(self: Reading, x: number): number {
  return x;
}

export function getStr(self: Reading, s: string): number {
  return s.length;
}

import { getNum, type Reading } from "./reading.js";

export class Use {
  constructor() { }

  run(r: Reading): number {
    return getNum(r, 1);
  }
}
