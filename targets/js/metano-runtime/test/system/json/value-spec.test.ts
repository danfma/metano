import { describe, expect, test } from "bun:test";
import { HashSet } from "#/system/collections/hash-set.ts";
import {
  type JsonConverter,
  JsonSerializer,
  type JsonTypeInfo,
  SerializerContext,
  type TypeSpec,
  type ValueSpec,
} from "#/system/json/index.ts";

// ─── Test Domain ─────────────────────────────────────────────────────────────

class Shot {
  constructor(
    readonly start: number,
    readonly end: number,
  ) {}
}

const shotSpec: TypeSpec<Shot> = {
  type: Shot,
  factory: (p) => new Shot(p.start as number, p.end as number),
  properties: [
    { ts: "start", json: "start", type: { kind: "primitive" } },
    { ts: "end", json: "end", type: { kind: "primitive" } },
  ],
};

const Mood = { Calm: "Calm", Loud: "Loud" } as const;

type Mood = (typeof Mood)[keyof typeof Mood];

enum Level {
  Low = 0,
  High = 1,
}

type EpisodeId = string & { readonly __brand: "EpisodeId" };

const EpisodeId = {
  create: (value: string) => value as EpisodeId,
};

/** Mirrors a generated context: value roots go through `createValueSpec`. */
class ShotContext extends SerializerContext {
  private _shot?: TypeSpec<Shot>;

  private _shotArray?: ValueSpec<Shot[] | null>;

  get shot(): TypeSpec<Shot> {
    return (this._shot ??= this.createSpec(shotSpec));
  }

  get shotArray(): ValueSpec<Shot[] | null> {
    return (this._shotArray ??= this.createValueSpec({
      descriptor: { kind: "array", element: { kind: "ref", spec: () => this.shot } },
    }));
  }
}

// ─── Round trips ─────────────────────────────────────────────────────────────

describe("JsonSerializer — ValueSpec round trips", () => {
  const ctx = new ShotContext();

  test("array of ref serializes each element through the element spec", () => {
    const json = JsonSerializer.serialize([new Shot(0, 1.5), new Shot(1.5, 3)], ctx.shotArray);

    expect(json).toEqual([
      { start: 0, end: 1.5 },
      { start: 1.5, end: 3 },
    ]);
  });

  test("array of ref deserializes into instances", () => {
    const shots = JsonSerializer.deserialize([{ start: 2, end: 4 }], ctx.shotArray);

    expect(shots).toHaveLength(1);
    expect(shots?.[0]).toBeInstanceOf(Shot);
    expect(shots?.[0]?.end).toBe(4);
  });

  test("array root reads JSON null as null", () => {
    expect(JsonSerializer.deserialize(null, ctx.shotArray)).toBeNull();
  });

  test("map root converts to and from a JSON object", () => {
    const spec: ValueSpec<Map<string, Shot> | null> = {
      descriptor: {
        kind: "map",
        key: { kind: "primitive" },
        value: { kind: "ref", spec: () => shotSpec },
      },
    };

    const json = JsonSerializer.serialize(new Map([["intro", new Shot(0, 1)]]), spec);
    const restored = JsonSerializer.deserialize(json, spec);

    expect(json).toEqual({ intro: { start: 0, end: 1 } });
    expect(restored?.get("intro")).toBeInstanceOf(Shot);
  });

  test("hash-set root converts to and from a JSON array", () => {
    const spec: ValueSpec<HashSet<string> | null> = {
      descriptor: { kind: "hashSet", element: { kind: "primitive" } },
    };

    const json = JsonSerializer.serialize(new HashSet(["a", "b"]), spec);
    const restored = JsonSerializer.deserialize(json, spec);

    expect(json).toEqual(["a", "b"]);
    expect(restored).toBeInstanceOf(HashSet);
    expect(restored?.has("b")).toBe(true);
  });

  test("nullable root passes null through and converts a value", () => {
    const spec: ValueSpec<number | null> = {
      descriptor: { kind: "nullable", inner: { kind: "primitive" } },
    };

    expect(JsonSerializer.serialize(null, spec)).toBeNull();
    expect(JsonSerializer.deserialize(null, spec)).toBeNull();
    expect(JsonSerializer.deserialize(7, spec)).toBe(7);
  });

  test("primitive root passes the value through", () => {
    const spec: ValueSpec<number> = { descriptor: { kind: "primitive" } };

    expect(JsonSerializer.serialize(42, spec)).toBe(42);
    expect(JsonSerializer.deserialize(42, spec)).toBe(42);
  });

  test("string enum root validates the value", () => {
    const spec: ValueSpec<Mood> = { descriptor: { kind: "enum", values: Mood } };

    expect(JsonSerializer.deserialize("Loud", spec)).toBe(Mood.Loud);
    expect(() => JsonSerializer.deserialize("Quiet", spec)).toThrow("Invalid enum value");
  });

  test("numeric enum root accepts the TS enum object and rejects its reverse-mapped names", () => {
    const spec: ValueSpec<Level> = { descriptor: { kind: "numericEnum", values: Level } };

    expect(JsonSerializer.serialize(Level.High, spec)).toBe(1);
    expect(JsonSerializer.deserialize(1, spec)).toBe(Level.High);
    expect(() => JsonSerializer.deserialize("High", spec)).toThrow("Invalid numeric enum value");
  });

  test("branded root deserializes through create", () => {
    const spec: ValueSpec<EpisodeId> = {
      descriptor: { kind: "branded", create: EpisodeId.create },
    };

    expect(JsonSerializer.deserialize("ep-1", spec)).toBe(EpisodeId.create("ep-1"));
    expect(JsonSerializer.serialize(EpisodeId.create("ep-1"), spec)).toBe("ep-1");
  });

  test("createValueSpec registers nothing for resolve()", () => {
    const fresh = new ShotContext();

    expect(fresh.shotArray.descriptor.kind).toBe("array");
    expect(fresh.resolve(Shot)).toBeUndefined();
  });

  test("JsonTypeInfo accepts both spec kinds", () => {
    const specs: JsonTypeInfo[] = [shotSpec, ctx.shotArray];

    expect(specs).toHaveLength(2);
  });
});

// ─── Iterables (FR-038) ──────────────────────────────────────────────────────

describe("JsonSerializer — iterable values", () => {
  const spec: ValueSpec<Iterable<number> | null> = {
    descriptor: { kind: "array", element: { kind: "primitive" } },
  };

  function* numbers() {
    yield 1;
    yield 2;
  }

  test("array descriptor serializes a Set as a JSON array", () => {
    expect(JsonSerializer.serialize(new Set([1, 2]), spec)).toEqual([1, 2]);
  });

  test("array descriptor serializes a generator as a JSON array", () => {
    expect(JsonSerializer.serialize(numbers(), spec)).toEqual([1, 2]);
  });

  test("hash-set descriptor serializes any iterable as a JSON array", () => {
    const setSpec: ValueSpec<Iterable<number> | null> = {
      descriptor: { kind: "hashSet", element: { kind: "primitive" } },
    };

    expect(JsonSerializer.serialize(new Set([3, 4]), setSpec)).toEqual([3, 4]);
  });

  test("hash-set descriptor deserializes a JSON array into a HashSet", () => {
    const setSpec: ValueSpec<HashSet<number> | null> = {
      descriptor: { kind: "hashSet", element: { kind: "primitive" } },
    };

    const restored = JsonSerializer.deserialize([5, 6], setSpec);

    expect(restored).toBeInstanceOf(HashSet);
    expect(restored?.has(6)).toBe(true);
  });

  test("an iterable property serializes as a JSON array", () => {
    class Track {
      constructor(readonly frames: Iterable<number>) {}
    }

    const trackSpec: TypeSpec<Track> = {
      type: Track,
      factory: (p) => new Track(p.frames as number[]),
      properties: [
        {
          ts: "frames",
          json: "frames",
          type: { kind: "array", element: { kind: "primitive" } },
        },
      ],
    };

    expect(JsonSerializer.serialize(new Track(numbers()), trackSpec)).toEqual({ frames: [1, 2] });
  });
});

// ─── Custom converters ───────────────────────────────────────────────────────

describe("JsonSerializer — custom converters inside a ValueSpec", () => {
  const secondsAsText: JsonConverter = {
    kind: "primitive",
    serialize: (value) => `${value as number}s`,
    deserialize: (value) => Number.parseFloat(value as string),
  };

  const spec: ValueSpec<number[] | null> = {
    descriptor: { kind: "array", element: { kind: "primitive" } },
  };

  test("the static serializer applies converters by kind to the elements", () => {
    expect(JsonSerializer.serialize([1, 2.5], spec, [secondsAsText])).toEqual(["1s", "2.5s"]);
    expect(JsonSerializer.deserialize(["3s"], spec, [secondsAsText])).toEqual([3]);
  });

  test("a bound serializer applies the context converters", () => {
    class SecondsContext extends SerializerContext {
      constructor() {
        super({ converters: [secondsAsText] });
      }
    }

    const bound = JsonSerializer.withContext(new SecondsContext());

    expect(bound.serialize([4], spec)).toEqual(["4s"]);
    expect(bound.deserialize(["5s"], spec)).toEqual([5]);
  });

  test("a converter for the root kind replaces the whole value conversion", () => {
    const arrayAsCount: JsonConverter = {
      kind: "array",
      serialize: (value) => (value as unknown[]).length,
      deserialize: (value) => Array.from({ length: value as number }, () => 0),
    };

    expect(JsonSerializer.serialize([1, 2, 3], spec, [arrayAsCount])).toBe(3);
    expect(JsonSerializer.deserialize(2, spec, [arrayAsCount])).toEqual([0, 0]);
  });
});
