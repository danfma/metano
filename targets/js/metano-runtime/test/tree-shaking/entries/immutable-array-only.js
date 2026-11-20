// `ImmutableArray` alone, as generated code imports it (spec 011 NFR-008, stage 011h).
import { ImmutableArray } from "./system/collections/immutable-array.js";

export function build(items) {
  return ImmutableArray.from(items);
}
