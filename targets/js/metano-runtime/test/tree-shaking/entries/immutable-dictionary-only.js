// `ImmutableDictionary` alone, as generated code imports it (spec 011 NFR-008, stage 011h).
import { ImmutableDictionary } from "./system/collections/immutable-dictionary.js";

export function build(items) {
  return ImmutableDictionary.from(items);
}
