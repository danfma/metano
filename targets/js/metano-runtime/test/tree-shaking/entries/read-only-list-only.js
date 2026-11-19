// `ReadOnlyList` alone, as generated code imports it (spec 011 NFR-008, stage 011h).
import { ReadOnlyList } from "./system/collections/read-only-list.js";

export function build(items) {
  return new ReadOnlyList(items);
}
