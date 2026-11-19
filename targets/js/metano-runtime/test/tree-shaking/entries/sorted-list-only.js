// `SortedList` alone, as generated code imports it (spec 011 NFR-008, stage 011h).
import { SortedList } from "./system/collections/sorted-list.js";

export function build(items) {
  return new SortedList(items);
}
