import { Tag, UId } from "tags/tags";

export class Consumer {
  constructor() { }

  run(): string {
    return Tag.sU(UId.create(1n)) + Tag.sL(1n) + Tag.nI(1);
  }
}
