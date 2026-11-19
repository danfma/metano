// `SortedDictionary` alone, as generated code imports it (spec 011 NFR-008, stage 011h).
import { SortedDictionary } from "./system/collections/sorted-dictionary.js";

export function build(items) {
  return new SortedDictionary(items);
}
