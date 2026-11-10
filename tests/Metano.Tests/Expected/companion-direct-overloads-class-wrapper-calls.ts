import { UId } from "./u-id";
import { Wrap } from "./wrap";

export class WrapCalls {
  constructor() { }

  run(): string {
    return Wrap.sU(UId.create(1n)) + Wrap.sL(1n);
  }
}
