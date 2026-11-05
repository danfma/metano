import { HashSet as HashSet$rt } from "metano-runtime";

export class HashSet {
  constructor() { }

  has(x: number): boolean {
    return true;
  }
}

export class Holder {
  items: HashSet$rt<number> = new HashSet$rt<number>();

  constructor() { }

  add(x: number): boolean {
    this.items.add(x);

    return true;
  }
}
