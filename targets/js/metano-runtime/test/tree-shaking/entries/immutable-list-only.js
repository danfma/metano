// `ImmutableList` alone, as generated code imports it (spec 011 NFR-008, stage 011h).
import { ImmutableList } from "./system/collections/immutable-list.js";

export function build(items) {
  return ImmutableList.from(items);
}
