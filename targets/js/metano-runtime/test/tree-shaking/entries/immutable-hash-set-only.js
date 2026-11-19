// `ImmutableHashSet` alone, as generated code imports it (spec 011 NFR-008, stage 011h).
import { ImmutableHashSet } from "./system/collections/immutable-hash-set.js";

export function build(items) {
  return ImmutableHashSet.from(items);
}
