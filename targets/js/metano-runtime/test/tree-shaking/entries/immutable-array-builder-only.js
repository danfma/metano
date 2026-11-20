// `ImmutableArray.Builder` alone, as generated code imports it (spec 011 NFR-008, stage 011h).
import { ImmutableArrayBuilder, toImmutableArray } from "./system/collections/immutable-array-builder.js";

export function build(items) {
  const builder = new ImmutableArrayBuilder(items);

  builder.add(0);

  return toImmutableArray(builder);
}
