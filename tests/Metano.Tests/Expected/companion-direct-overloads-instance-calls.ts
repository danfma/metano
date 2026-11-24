import type { Tag } from "./tag.js";
import { UId } from "./u-id.js";

export class InstanceCalls {
  constructor() { }

  run(tag: Tag): string {
    return (tag.i(1n) ?? "") + (tag.i(UId.create(1n)) ?? "");
  }
}
