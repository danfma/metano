import { describe, expect, test } from "bun:test";
import {
  JsonException,
  JsonSerializer,
  type TypeSpec,
  type ValueSpec,
} from "#/system/json/index.ts";
import { expectJsonException } from "./json-exception-assertions.ts";

class Tag {
  constructor(readonly label: string) {}
}

const tagSpec: TypeSpec<Tag> = {
  type: Tag,
  factory: (p) => new Tag(p.label as string),
  properties: [{ ts: "label", json: "label", type: { kind: "primitive" } }],
};

const plainSpec: TypeSpec<{ label: string }> = {
  factory: (p) => ({ label: p.label as string }),
  properties: [{ ts: "label", json: "label", type: { kind: "primitive" } }],
};

const tagsSpec: ValueSpec<Tag[] | null> = {
  descriptor: { kind: "array", element: { kind: "ref", spec: () => tagSpec } },
};

const tagSetSpec: ValueSpec<unknown> = {
  descriptor: { kind: "hashSet", element: { kind: "primitive" } },
};

const nullableTagsSpec: ValueSpec<Tag[] | null> = {
  descriptor: { kind: "nullable", inner: tagsSpec.descriptor },
};

describe("JsonException", () => {
  test("is an Error named after its class", () => {
    const error = new JsonException("bad");

    expect(error).toBeInstanceOf(Error);
    expect(error.name).toBe("JsonException");
    expect(error.message).toBe("bad");
  });
});

describe("JsonSerializer — object contract errors", () => {
  test("a non-object value names the contract type", () => {
    expectJsonException(
      () => JsonSerializer.deserialize(5, tagSpec),
      "The JSON value could not be converted to Tag.",
    );
  });

  test("an array for an object contract names the contract type", () => {
    expectJsonException(
      () => JsonSerializer.deserialize([], tagSpec),
      "The JSON value could not be converted to Tag.",
    );
  });

  test("a [PlainObject] contract without a constructor reports Object", () => {
    expectJsonException(
      () => JsonSerializer.deserialize("x", plainSpec),
      "The JSON value could not be converted to Object.",
    );
  });

  test("a missing required property", () => {
    expectJsonException(
      () => JsonSerializer.deserialize({}, tagSpec),
      'Missing required property: "label"',
    );
  });

  test("an invalid string enum value", () => {
    const moodSpec: ValueSpec<string> = {
      descriptor: { kind: "enum", values: { Calm: "Calm" } },
    };

    expectJsonException(
      () => JsonSerializer.deserialize("Loud", moodSpec),
      'Invalid enum value: "Loud". Expected one of: Calm',
    );
  });

  test("an invalid numeric enum value", () => {
    const levelSpec: ValueSpec<number> = {
      descriptor: { kind: "numericEnum", values: { Low: 0, 0: "Low" } },
    };

    expectJsonException(
      () => JsonSerializer.deserialize(3, levelSpec),
      "Invalid numeric enum value: 3. Expected one of: 0",
    );
  });
});

describe("JsonSerializer — non-array input to a collection (#425)", () => {
  test.each([
    [{ label: "x" }, "object"],
    ["x", "string"],
    [5, "number"],
    [true, "boolean"],
  ])("an array root rejects %p", (input, kind) => {
    expectJsonException(
      () => JsonSerializer.deserialize(input, tagsSpec),
      `Expected array for deserialization, got ${kind}`,
    );
  });

  test.each([
    [{}, "object"],
    ["x", "string"],
    [5, "number"],
  ])("a hash-set root rejects %p", (input, kind) => {
    expectJsonException(
      () => JsonSerializer.deserialize(input, tagSetSpec),
      `Expected array for deserialization, got ${kind}`,
    );
  });

  test("an array property rejects an object", () => {
    const shelfSpec: TypeSpec<{ tags: Tag[] }> = {
      factory: (p) => ({ tags: p.tags as Tag[] }),
      properties: [{ ts: "tags", json: "tags", type: tagsSpec.descriptor }],
    };

    expectJsonException(
      () => JsonSerializer.deserialize({ tags: { label: "x" } }, shelfSpec),
      "Expected array for deserialization, got object",
    );
  });

  test("null still reads as null", () => {
    expect(JsonSerializer.deserialize(null, tagsSpec)).toBeNull();
    expect(JsonSerializer.deserialize(null, tagSetSpec)).toBeNull();
    expect(JsonSerializer.deserialize(null, nullableTagsSpec)).toBeNull();
  });
});
