// `ConcurrentDictionary` alone, as generated code imports it (spec 011 NFR-008, stage 011h).
import { ConcurrentDictionary } from "./system/collections/concurrent-dictionary.js";

export function build(items) {
  return new ConcurrentDictionary(items);
}
