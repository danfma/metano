import { describe, expect, test } from "bun:test";

import {
  ImmutableDictionaryBuilder,
  toImmutableDictionary,
} from "#/system/collections/immutable-dictionary-builder.ts";

describe("ImmutableDictionaryBuilder (W4-11 condition 4: never alias)", () => {
  test("ToImmutable, then a further builder mutation changes no immutable value", () => {
    const builder = new ImmutableDictionaryBuilder<string, number>();

    builder.set("a", 1);

    const dictionary = toImmutableDictionary(builder);

    builder.set("a", 2);
    builder.set("b", 3);

    expect([...dictionary]).toEqual([["a", 1]]);
  });
});
