import { describe, expect, test } from "bun:test";
import { JsonSerializer, type TypeSpec, type ValueSpec } from "#/system/json/index.ts";
import { errorOf, expectJsonException } from "./json-exception-assertions.ts";

// Boundaries measured with System.Text.Json on .NET 10 (MaxDepth 64): a
// chain of 64 nested objects, or of 32 object/array pairs, is written and
// read; one more container throws. The write boundary is the source-generated
// fast path's (containers only), the write error is the metadata mode's
// (DECIDED-20 b).

const WRITE_MESSAGE =
  "A possible object cycle was detected. This can either be due to a cycle or if the object " +
  "depth is larger than the maximum allowed depth of 64. Consider using " +
  "ReferenceHandler.Preserve on JsonSerializerOptions to support cycles.";

const READ_OBJECT_MESSAGE =
  "The maximum configured depth of 64 has been exceeded. Cannot read next JSON object.";

const READ_ARRAY_MESSAGE =
  "The maximum configured depth of 64 has been exceeded. Cannot read next JSON array.";

class Node {
  constructor(public next: Node | null) {}
}

const nodeSpec: TypeSpec<Node> = {
  type: Node,
  factory: (p) => new Node(p.next as Node | null),
  properties: [
    {
      ts: "next",
      json: "Next",
      type: { kind: "ref", spec: () => nodeSpec },
      optional: true,
    },
  ],
};

class ValueNode {
  constructor(
    readonly value: number,
    readonly next: ValueNode | null,
  ) {}
}

const valueNodeSpec: TypeSpec<ValueNode> = {
  type: ValueNode,
  factory: (p) => new ValueNode(p.value as number, p.next as ValueNode | null),
  properties: [
    { ts: "value", json: "Value", type: { kind: "primitive" } },
    {
      ts: "next",
      json: "Next",
      type: { kind: "ref", spec: () => valueNodeSpec },
      optional: true,
    },
  ],
};

class Branch {
  constructor(readonly items: Branch[]) {}
}

const branchSpec: TypeSpec<Branch> = {
  type: Branch,
  factory: (p) => new Branch(p.items as Branch[]),
  properties: [
    {
      ts: "items",
      json: "Items",
      type: { kind: "array", element: { kind: "ref", spec: () => branchSpec } },
    },
  ],
};

/** The spec of `depth` nested arrays of numbers (`number[][]…`). */
function nestedArraysSpec(depth: number): ValueSpec<unknown> {
  return { descriptor: nestedArrayDescriptor(depth) };
}

function nestedArrayDescriptor(depth: number): ValueSpec["descriptor"] {
  return depth === 0
    ? { kind: "primitive" }
    : { kind: "array", element: nestedArrayDescriptor(depth - 1) };
}

/** `count` nested nodes; the innermost has no next node. */
function nodeChain(count: number): Node {
  return Array.from({ length: count - 1 }).reduce<Node>((next) => new Node(next), new Node(null));
}

/** The JSON of `count` nested nodes. */
function nodeJson(count: number): unknown {
  return Array.from({ length: count }).reduce<unknown>((next) => ({ Next: next }), null);
}

/** `count` nested value nodes, each carrying the number 1. */
function valueNodeChain(count: number): ValueNode {
  return Array.from({ length: count - 1 }).reduce<ValueNode>(
    (next) => new ValueNode(1, next),
    new ValueNode(1, null),
  );
}

/** `count` branches, each the only item of the next. */
function branchChain(count: number): Branch {
  return Array.from({ length: count - 1 }).reduce<Branch>(
    (inner) => new Branch([inner]),
    new Branch([]),
  );
}

function branchJson(count: number): unknown {
  return Array.from({ length: count - 1 }).reduce<unknown>((inner) => ({ Items: [inner] }), {
    Items: [],
  });
}

/** `count` nested JSON arrays around the number 1. */
function nestedArrays(count: number): unknown {
  return Array.from({ length: count }).reduce<unknown>((inner) => [inner], 1);
}

describe("JsonSerializer — write depth (FR-016)", () => {
  test("writes 64 nested objects", () => {
    expect(() => JsonSerializer.serialize(nodeChain(64), nodeSpec)).not.toThrow();
  });

  test("the 65th nested object throws", () => {
    expectJsonException(() => JsonSerializer.serialize(nodeChain(65), nodeSpec), WRITE_MESSAGE);
  });

  test("a cycle throws instead of overflowing the stack", () => {
    const node = new Node(null);
    node.next = node;

    expectJsonException(() => JsonSerializer.serialize(node, nodeSpec), WRITE_MESSAGE);
  });

  // A hybrid of STJ's modes (DECIDED-20 b): the source-generated fast path
  // writes a scalar inside the 64th object and throws only at the 65th
  // container, which Metano follows; STJ's metadata and reflection modes
  // already throw on that scalar.
  test("a scalar inside the 64th nested object is written", () => {
    expect(() => JsonSerializer.serialize(valueNodeChain(64), valueNodeSpec)).not.toThrow();

    expectJsonException(
      () => JsonSerializer.serialize(valueNodeChain(65), valueNodeSpec),
      WRITE_MESSAGE,
    );
  });

  test("arrays count: 32 object/array pairs are written, 33 throw", () => {
    expect(() => JsonSerializer.serialize(branchChain(32), branchSpec)).not.toThrow();
    expectJsonException(() => JsonSerializer.serialize(branchChain(33), branchSpec), WRITE_MESSAGE);
  });

  test("a value root of nested arrays", () => {
    expect(() => JsonSerializer.serialize(nestedArrays(64), nestedArraysSpec(64))).not.toThrow();

    expectJsonException(
      () => JsonSerializer.serialize(nestedArrays(65), nestedArraysSpec(65)),
      WRITE_MESSAGE,
    );
  });

  test("the depth resets after a failed call", () => {
    errorOf(() => JsonSerializer.serialize(nodeChain(65), nodeSpec));

    expect(() => JsonSerializer.serialize(nodeChain(64), nodeSpec)).not.toThrow();
  });
});

describe("JsonSerializer — read depth (FR-016)", () => {
  test("reads 64 nested objects", () => {
    expect(JsonSerializer.deserialize(nodeJson(64), nodeSpec)).toEqual(nodeChain(64));
  });

  test("the 65th nested object throws", () => {
    expectJsonException(
      () => JsonSerializer.deserialize(nodeJson(65), nodeSpec),
      READ_OBJECT_MESSAGE,
    );
  });

  test("arrays count: 32 object/array pairs are read, 33 throw", () => {
    expect(() => JsonSerializer.deserialize(branchJson(32), branchSpec)).not.toThrow();

    expectJsonException(
      () => JsonSerializer.deserialize(branchJson(33), branchSpec),
      READ_OBJECT_MESSAGE,
    );
  });

  test("a value root of nested arrays names the array", () => {
    expect(() => JsonSerializer.deserialize(nestedArrays(64), nestedArraysSpec(64))).not.toThrow();

    expectJsonException(
      () => JsonSerializer.deserialize(nestedArrays(65), nestedArraysSpec(65)),
      READ_ARRAY_MESSAGE,
    );
  });

  test("the depth resets after a failed call", () => {
    errorOf(() => JsonSerializer.deserialize(nodeJson(65), nodeSpec));

    expect(() => JsonSerializer.deserialize(nodeJson(64), nodeSpec)).not.toThrow();
  });
});
