import type { Tag } from "./tag";
import { UId } from "./u-id";

export class InstanceCalls {
  constructor() { }

  run(tag: Tag): string {
    return tag.i(1n) + tag.i(UId.create(1n));
  }
}
