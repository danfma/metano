import { UId } from "./u-id.js";
import { Wrap } from "./wrap.js";

export class WrapCalls {
  run(): string {
    return (Wrap.sU(UId.create(1n)) ?? "") + (Wrap.sL(1n) ?? "");
  }
}
