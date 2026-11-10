import { Tag } from "./tag";
import { UId } from "./u-id";

export class Calls {
  constructor() { }

  run(): string {
    const pick = Tag.sU;

    return Tag.sU(UId.create(1n)) + Tag.sL(1n) + pick(UId.create(3n)) + Tag.anyValue<string>("x") + Tag.anyValue<number>(1) + Tag.nS("a") + Tag.nI(1);
  }
}
