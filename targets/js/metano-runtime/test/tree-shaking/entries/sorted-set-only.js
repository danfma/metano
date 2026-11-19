// `SortedSet` alone, as generated code imports it (spec 011 NFR-008, stage 011h).
import { SortedSet } from "./system/collections/sorted-set.js";

export function build(items) {
  return new SortedSet(items);
}
