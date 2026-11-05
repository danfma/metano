import { describe, expect, test } from "bun:test";

import { Temporal } from "@js-temporal/polyfill";

import {
  type ArrayDescriptor,
  JsonSerializer,
  type NumericEnumDescriptor,
  type PrimitiveDescriptor,
  type TemporalDescriptor,
  type TypeDescriptor,
  type TypeSpec,
  type ValueSpec,
} from "#/system/json/index.ts";
import { cannotConvertMessage, expectJsonException } from "./json-exception-assertions.ts";

// JSON `null` reaching a non-nullable value-type member or element, measured
// on System.Text.Json (.NET 10, source generated context, #471): a property
// names the declaring contract, an element, a dictionary value or a value
// root names the element type, and a struct names itself. A nullable slot
// keeps reading `null`.

// ─── Test Domain ─────────────────────────────────────────────────────────────

class HolderInt {
  constructor(readonly n: number) {}
}

class HolderTime {
  constructor(readonly at: Temporal.PlainDateTime) {}
}

class HolderChar {
  constructor(readonly c: string) {}
}

class SPoint {
  constructor(
    readonly x: number,
    readonly y: number,
  ) {}
}

enum Level {
  Low = 0,
  High = 1,
}

type EpisodeId = string & { readonly __brand: "EpisodeId" };

const EpisodeId = { create: (value: string) => value as EpisodeId };

const int32: PrimitiveDescriptor = { kind: "primitive", numberType: "System.Int32" };

const boolean: PrimitiveDescriptor = { kind: "primitive", typeName: "System.Boolean" };

const char: PrimitiveDescriptor = { kind: "primitive", typeName: "System.Char" };

const dateTime: TemporalDescriptor = {
  kind: "temporal",
  parse: Temporal.PlainDateTime.from,
  typeName: "System.DateTime",
};

const levelEnum: NumericEnumDescriptor = {
  kind: "numericEnum",
  values: Level,
  typeName: "Probe.Level",
};

const episodeId: TypeDescriptor = {
  kind: "branded",
  create: EpisodeId.create,
  typeName: "Probe.EpisodeId",
};

const holderIntSpec: TypeSpec<HolderInt> = {
  type: HolderInt,
  factory: (properties) => new HolderInt(properties.n as number),
  properties: [{ ts: "n", json: "N", type: int32 }],
};

const holderTimeSpec: TypeSpec<HolderTime> = {
  type: HolderTime,
  factory: (properties) => new HolderTime(properties.at as Temporal.PlainDateTime),
  properties: [{ ts: "at", json: "At", type: dateTime }],
};

const holderCharSpec: TypeSpec<HolderChar> = {
  type: HolderChar,
  factory: (properties) => new HolderChar(properties.c as string),
  properties: [{ ts: "c", json: "C", type: char }],
};

const listInt32: ValueSpec<number[]> = {
  descriptor: { kind: "array", element: int32 } satisfies ArrayDescriptor,
};

const listNullableInt32: ValueSpec<(number | null)[]> = {
  descriptor: { kind: "array", element: { kind: "nullable", inner: int32 } },
};

const setInt32: ValueSpec<Set<number>> = {
  descriptor: { kind: "hashSet", element: int32 },
};

const dictionaryStringInt32: ValueSpec<Map<string, number>> = {
  descriptor: { kind: "map", key: { kind: "primitive" }, value: int32 },
};

const dictionaryStringNullableInt32: ValueSpec<Map<string, number | null>> = {
  descriptor: { kind: "map", key: { kind: "primitive" }, value: { kind: "nullable", inner: int32 } },
};

const int32Array: ValueSpec<number[]> = {
  descriptor: { kind: "array", element: int32 },
};

const listLevel: ValueSpec<Level[]> = {
  descriptor: { kind: "array", element: levelEnum },
};

const listEpisodeId: ValueSpec<EpisodeId[]> = {
  descriptor: { kind: "array", element: episodeId },
};

// ─── Properties ──────────────────────────────────────────────────────────────

describe("a null on a non-nullable value-type property names the contract", () => {
  test("an int property", () => {
    expectJsonException(
      () => JsonSerializer.deserialize({ N: null }, holderIntSpec),
      cannotConvertMessage("HolderInt"),
    );
  });

  test("a temporal property", () => {
    expectJsonException(
      () => JsonSerializer.deserialize({ At: null }, holderTimeSpec),
      cannotConvertMessage("HolderTime"),
    );
  });

  test("a char property", () => {
    expectJsonException(
      () => JsonSerializer.deserialize({ C: null }, holderCharSpec),
      cannotConvertMessage("HolderChar"),
    );
  });
});

// ─── Elements and dictionary values ──────────────────────────────────────────

describe("a null element or value names the element type", () => {
  test("a list element", () => {
    expectJsonException(
      () => JsonSerializer.deserialize([1, null], listInt32),
      cannotConvertMessage("System.Int32"),
    );
  });

  test("a set element", () => {
    expectJsonException(
      () => JsonSerializer.deserialize([1, null], setInt32),
      cannotConvertMessage("System.Int32"),
    );
  });

  test("a dictionary value", () => {
    expectJsonException(
      () => JsonSerializer.deserialize({ a: 1, b: null }, dictionaryStringInt32),
      cannotConvertMessage("System.Int32"),
    );
  });

  test("an array element", () => {
    expectJsonException(
      () => JsonSerializer.deserialize([1, null], int32Array),
      cannotConvertMessage("System.Int32"),
    );
  });

  test("an enum element names the enum", () => {
    expectJsonException(
      () => JsonSerializer.deserialize([0, null], listLevel),
      cannotConvertMessage("Probe.Level"),
    );
  });

  test("a branded element names the branded type", () => {
    expectJsonException(
      () => JsonSerializer.deserialize(["a", null], listEpisodeId),
      cannotConvertMessage("Probe.EpisodeId"),
    );
  });
});

// ─── Nullable slots ──────────────────────────────────────────────────────────

describe("nullable slots keep reading null", () => {
  test("a nullable list element", () => {
    expect(JsonSerializer.deserialize([1, null], listNullableInt32)).toEqual([1, null]);
  });

  test("a nullable dictionary value", () => {
    const map = JsonSerializer.deserialize({ a: null }, dictionaryStringNullableInt32) as Map<
      string,
      number | null
    >;

    expect(map.get("a")).toBeNull();
  });

  test("a nullable property", () => {
    const spec: TypeSpec<HolderInt> = {
      type: HolderInt,
      factory: (properties) => new HolderInt(properties.n as number),
      properties: [{ ts: "n", json: "N", type: { kind: "nullable", inner: int32 }, optional: true }],
    };

    expect(JsonSerializer.deserialize({ N: null }, spec)?.n).toBeNull();
  });
});

// ─── char ────────────────────────────────────────────────────────────────────

describe("a char reads one UTF-16 code unit only", () => {
  test("reads a single character", () => {
    expect(JsonSerializer.deserialize({ C: "a" }, holderCharSpec)?.c).toBe("a");
  });

  test("rejects two characters naming the contract", () => {
    expectJsonException(
      () => JsonSerializer.deserialize({ C: "ab" }, holderCharSpec),
      cannotConvertMessage("HolderChar"),
    );
  });

  test("rejects a surrogate pair naming the contract", () => {
    expectJsonException(
      () => JsonSerializer.deserialize({ C: "😀" }, holderCharSpec),
      cannotConvertMessage("HolderChar"),
    );
  });

  test("rejects a non-string naming the contract", () => {
    expectJsonException(
      () => JsonSerializer.deserialize({ C: 5 }, holderCharSpec),
      cannotConvertMessage("HolderChar"),
    );
  });
});

// ─── value roots ─────────────────────────────────────────────────────────────

describe("a null value root names the root type", () => {
  const booleanRoot: ValueSpec<boolean> = {
    descriptor: boolean,
    valueType: true,
    typeName: "System.Boolean",
  };

  test("a boolean root", () => {
    expectJsonException(
      () => JsonSerializer.deserialize(null, booleanRoot),
      cannotConvertMessage("System.Boolean"),
    );
  });
});
