import { HashSet as HashSet$rt } from "metano-runtime/system/collections";

export class HashSet {
  has(x: number): boolean {
    return true;
  }
}

export class Holder {
  declare items: HashSet$rt<number>;

  constructor() {
    this.items = new HashSet$rt<number>();
  }

  add(x: number): boolean {
    this.items.add(x);

    return true;
  }
}
