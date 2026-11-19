import { describe, expect, test } from "bun:test";
import { JsonSerializer, type TypeSpec } from "#/system/json/index.ts";

// `PropertySpec.omitWhenNull` (`JsonIgnoreCondition.WhenWritingNull`, #667),
// measured against a source-generated System.Text.Json context on .NET 10
// (SDK 10.0.401, runtime 10.0.12) with `DefaultIgnoreCondition = WhenWritingNull`:
//   record Cycle(string Id, string? InferredFrom = null, int? Count = null)
//     Cycle("cy-1")          → {"id":"cy-1"}
//     Cycle("cy-1", "x", 0)  → {"id":"cy-1","inferredFrom":"x","count":0}
//   Mixed(Id, [Never] Keep, [WhenWritingNull] Omit, Opt) with every optional null
//     → {"id":"m","keep":null}   (Web context without a default: also "opt":null)

class Mixed {
  constructor(
    readonly id: string,
    readonly keep: string | null = null,
    readonly omit: string | null = null,
    readonly opt: number | null = null,
  ) {}
}

const nullable = { kind: "nullable", inner: { kind: "primitive" } } as const;

const spec: TypeSpec<Mixed> = {
  type: Mixed,
  factory: (p) =>
    new Mixed(
      p.id as string,
      p.keep as string | null,
      p.omit as string | null,
      p.opt as number | null,
    ),
  properties: [
    { ts: "id", json: "id", type: { kind: "primitive" } },
    { ts: "keep", json: "keep", type: nullable, optional: true },
    { ts: "omit", json: "omit", type: nullable, optional: true, omitWhenNull: true },
    { ts: "opt", json: "opt", type: nullable, optional: true, omitWhenNull: true },
  ],
};

describe("JsonSerializer — omitWhenNull", () => {
  test("leaves a null member out and keeps the others", () => {
    const json = JSON.stringify(JsonSerializer.serialize(new Mixed("m"), spec));

    expect(json).toBe('{"id":"m","keep":null}');
  });

  test("writes a member that has a value", () => {
    const json = JSON.stringify(JsonSerializer.serialize(new Mixed("m", "k", "o", 0), spec));

    expect(json).toBe('{"id":"m","keep":"k","omit":"o","opt":0}');
  });

  test("reads a payload without the omitted member as null", () => {
    const read = JsonSerializer.deserialize({ id: "m", keep: null }, spec)!;

    expect(read.omit).toBeNull();
    expect(read.opt).toBeNull();
  });
});
