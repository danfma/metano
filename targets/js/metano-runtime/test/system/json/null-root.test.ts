import { describe, expect, test } from "bun:test";
import { Decimal } from "decimal.js";
import {
  type JsonConverter,
  JsonSerializer,
  SerializerContext,
  type TypeSpec,
  type ValueSpec,
} from "#/system/json/index.ts";
import { cannotConvertMessage, expectJsonException } from "./json-exception-assertions.ts";

// JSON `null` at a root, as measured on System.Text.Json (.NET 10, source
// generated context, FR-037): `null` for every reference type and for
// `Nullable<T>`; "The JSON value could not be converted to <Type>." for every
// value type; and the empty text has no JSON token at all.

// ─── Test Domain ─────────────────────────────────────────────────────────────

class Cls {
  constructor(readonly x: number) {}
}

class SPoint {
  constructor(
    readonly x: number,
    readonly y: number,
  ) {}
}

abstract class Clip {
  constructor(readonly seconds: number) {}
}

class ImageClip extends Clip {}

enum Level {
  Low = 0,
  High = 1,
}

type EpisodeId = string & { readonly __brand: "EpisodeId" };

const EpisodeId = { create: (value: string) => value as EpisodeId };

interface StoredNote {
  readonly text: string;
}

const NO_TOKENS_MESSAGE =
  "The input does not contain any JSON tokens. Expected the input to start with a valid JSON " +
  "token, when isFinalBlock is true.";

/** Mirrors a generated context: struct contracts and value-type roots carry `valueType`. */
class NullRootContext extends SerializerContext {
  private _cls?: TypeSpec<Cls>;
  private _sPoint?: TypeSpec<SPoint>;
  private _clip?: TypeSpec<Clip>;
  private _imageClip?: TypeSpec<ImageClip>;
  private _storedNote?: TypeSpec<StoredNote>;
  private _int32?: ValueSpec<number>;
  private _nullableInt32?: ValueSpec<number | null>;
  private _string?: ValueSpec<string | null>;
  private _listInt32?: ValueSpec<number[] | null>;
  private _dictionaryStringInt32?: ValueSpec<Map<string, number> | null>;
  private _decimal?: ValueSpec<Decimal>;
  private _level?: ValueSpec<Level>;
  private _episodeId?: ValueSpec<EpisodeId>;

  get cls(): TypeSpec<Cls> {
    return (this._cls ??= this.createSpec({
      type: Cls,
      factory: (p) => new Cls(p.x as number),
      properties: [{ ts: "x", json: "X", type: { kind: "primitive" } }],
    }));
  }

  get sPoint(): TypeSpec<SPoint> {
    return (this._sPoint ??= this.createSpec({
      type: SPoint,
      factory: (p) => new SPoint(p.x as number, p.y as number),
      properties: [
        { ts: "x", json: "X", type: { kind: "primitive" } },
        { ts: "y", json: "Y", type: { kind: "primitive" } },
      ],
      valueType: true,
    }));
  }

  get clip(): TypeSpec<Clip> {
    return (this._clip ??= this.createSpec({
      type: Clip,
      factory: () => {
        throw new Error("Cannot deserialize abstract type Clip");
      },
      properties: [{ ts: "seconds", json: "Seconds", type: { kind: "primitive" } }],
      polymorphism: {
        discriminator: "kind",
        abstract: true,
        derived: [{ tag: "image", spec: () => this.imageClip }],
      },
    }));
  }

  get imageClip(): TypeSpec<ImageClip> {
    return (this._imageClip ??= this.createSpec({
      type: ImageClip,
      base: this.clip,
      factory: (p) => new ImageClip(p.seconds as number),
      properties: [],
    }));
  }

  get storedNote(): TypeSpec<StoredNote> {
    return (this._storedNote ??= this.createSpec({
      factory: (p) => ({ text: p.text as string }),
      properties: [{ ts: "text", json: "Text", type: { kind: "primitive" } }],
    }));
  }

  get int32(): ValueSpec<number> {
    return (this._int32 ??= this.createValueSpec({
      descriptor: { kind: "primitive" },
      valueType: true,
    }));
  }

  get nullableInt32(): ValueSpec<number | null> {
    return (this._nullableInt32 ??= this.createValueSpec({
      descriptor: { kind: "nullable", inner: { kind: "primitive" } },
    }));
  }

  get string(): ValueSpec<string | null> {
    return (this._string ??= this.createValueSpec({ descriptor: { kind: "primitive" } }));
  }

  get listInt32(): ValueSpec<number[] | null> {
    return (this._listInt32 ??= this.createValueSpec({
      descriptor: { kind: "array", element: { kind: "primitive" } },
    }));
  }

  get dictionaryStringInt32(): ValueSpec<Map<string, number> | null> {
    return (this._dictionaryStringInt32 ??= this.createValueSpec({
      descriptor: { kind: "map", key: { kind: "primitive" }, value: { kind: "primitive" } },
    }));
  }

  get decimal(): ValueSpec<Decimal> {
    return (this._decimal ??= this.createValueSpec({
      descriptor: { kind: "decimal", type: Decimal },
      valueType: true,
    }));
  }

  get level(): ValueSpec<Level> {
    return (this._level ??= this.createValueSpec({
      descriptor: { kind: "numericEnum", values: Level },
      valueType: true,
    }));
  }

  get episodeId(): ValueSpec<EpisodeId> {
    return (this._episodeId ??= this.createValueSpec({
      descriptor: { kind: "branded", create: EpisodeId.create },
      valueType: true,
    }));
  }
}

/** A context that reads numbers from strings names each number's CLR type (FR-048). */
class WebNumbersContext extends SerializerContext {
  private _int32?: ValueSpec<number>;

  protected override get readOptions() {
    return { numberHandling: "allowReadingFromString" as const };
  }

  get int32(): ValueSpec<number> {
    return (this._int32 ??= this.createValueSpec({
      descriptor: { kind: "primitive", numberType: "System.Int32" },
      valueType: true,
    }));
  }
}

const ctx = new NullRootContext();

type Row<S> = readonly [name: string, spec: S];

// ─── Reference-type roots ────────────────────────────────────────────────────

describe("JSON null at a reference-type root reads as null", () => {
  const objectRoots: readonly Row<TypeSpec<unknown>>[] = [
    ["class contract", ctx.cls],
    ["abstract polymorphic base", ctx.clip],
    ["[PlainObject] contract", ctx.storedNote],
  ];

  const valueRoots: readonly Row<ValueSpec<unknown>>[] = [
    ["Nullable<int>", ctx.nullableInt32],
    ["string", ctx.string],
    ["List<int>", ctx.listInt32],
    ["Dictionary<string, int>", ctx.dictionaryStringInt32],
  ];

  test.each(objectRoots)("%s through every entry point", (_, spec) => {
    const bound = JsonSerializer.withContext(ctx);

    expect(JsonSerializer.deserialize(null, spec)).toBeNull();
    expect(JsonSerializer.deserializeText(" null ", spec)).toBeNull();
    expect(bound.deserialize(null, spec)).toBeNull();
    expect(bound.deserializeText("null", spec)).toBeNull();
  });

  test.each(valueRoots)("%s value root through every entry point", (_, spec) => {
    const bound = JsonSerializer.withContext(ctx);

    expect(JsonSerializer.deserialize(null, spec)).toBeNull();
    expect(JsonSerializer.deserializeText(" null ", spec)).toBeNull();
    expect(bound.deserialize(null, spec)).toBeNull();
    expect(bound.deserializeText("null", spec)).toBeNull();
  });

  test("a custom converter of the root's kind never sees the null", () => {
    const converters: JsonConverter[] = [
      {
        kind: "array",
        serialize: () => {
          throw new Error("serialize must not run");
        },
        deserialize: () => {
          throw new Error("deserialize must not run");
        },
      },
    ];

    expect(JsonSerializer.deserialize(null, ctx.listInt32, converters)).toBeNull();
  });
});

// ─── Value-type roots ────────────────────────────────────────────────────────

describe("JSON null at a value-type root throws JsonException", () => {
  test("a struct contract names its type", () => {
    expectJsonException(
      () => JsonSerializer.deserialize(null, ctx.sPoint),
      cannotConvertMessage("SPoint"),
    );

    expectJsonException(
      () => JsonSerializer.deserializeText("null", ctx.sPoint),
      cannotConvertMessage("SPoint"),
    );
  });

  test("a struct contract still reads an object", () => {
    expect(JsonSerializer.deserialize({ X: 1, Y: 2 }, ctx.sPoint)).toEqual(new SPoint(1, 2));
  });

  test("a decimal names System.Decimal on both read paths", () => {
    expectJsonException(
      () => JsonSerializer.deserialize(null, ctx.decimal),
      cannotConvertMessage("System.Decimal"),
    );

    expectJsonException(
      () => JsonSerializer.deserializeText("null", ctx.decimal),
      cannotConvertMessage("System.Decimal"),
    );
  });

  test("a number names its numberType when the descriptor carries it", () => {
    const web = new WebNumbersContext();

    expectJsonException(
      () => JsonSerializer.deserialize(null, web.int32),
      cannotConvertMessage("System.Int32"),
    );
  });

  const unnamedValueTypes: readonly Row<ValueSpec<unknown>>[] = [
    ["int without a numberType", ctx.int32],
    ["numeric enum", ctx.level],
    ["branded type", ctx.episodeId],
  ];

  test.each(unnamedValueTypes)("a %s is named generically", (_, spec) => {
    expectJsonException(
      () => JsonSerializer.deserialize(null, spec),
      cannotConvertMessage("a value type"),
    );
  });

  test("a bound serializer throws as well", () => {
    const bound = JsonSerializer.withContext(ctx);

    expectJsonException(() => bound.deserialize(null, ctx.sPoint), cannotConvertMessage("SPoint"));

    expectJsonException(
      () => bound.deserializeText("null", ctx.int32),
      cannotConvertMessage("a value type"),
    );
  });

  test("the check runs before a custom converter of the root's kind", () => {
    const converters: JsonConverter[] = [
      { kind: "primitive", serialize: (value) => value, deserialize: () => 0 },
    ];

    expectJsonException(
      () => JsonSerializer.deserialize(null, ctx.int32, converters),
      cannotConvertMessage("a value type"),
    );
  });
});

// ─── Text without a JSON token ───────────────────────────────────────────────

describe("deserializeText of a text without a JSON token", () => {
  test.each(["", "  ", "\n"])("%j throws the reader's message for every root", (text) => {
    expectJsonException(() => JsonSerializer.deserializeText(text, ctx.cls), NO_TOKENS_MESSAGE);
    expectJsonException(() => JsonSerializer.deserializeText(text, ctx.sPoint), NO_TOKENS_MESSAGE);

    expectJsonException(
      () => JsonSerializer.deserializeText(text, ctx.nullableInt32),
      NO_TOKENS_MESSAGE,
    );

    expectJsonException(() => JsonSerializer.deserializeText(text, ctx.int32), NO_TOKENS_MESSAGE);
  });
});

// ─── Writes ──────────────────────────────────────────────────────────────────

describe("a null value at a root writes JSON null", () => {
  const objectRoots: readonly Row<TypeSpec<unknown>>[] = [
    ["class contract", ctx.cls],
    ["abstract polymorphic base", ctx.clip],
    ["[PlainObject] contract", ctx.storedNote],
  ];

  const valueRoots: readonly Row<ValueSpec<unknown>>[] = [
    ["Nullable<int>", ctx.nullableInt32],
    ["string", ctx.string],
    ["List<int>", ctx.listInt32],
  ];

  test.each(objectRoots)("%s", (_, spec) => {
    expect(JsonSerializer.serialize(null, spec)).toBeNull();
    expect(JsonSerializer.withContext(ctx).serialize(null, spec)).toBeNull();
  });

  test.each(valueRoots)("%s value root", (_, spec) => {
    expect(JsonSerializer.serialize(null, spec)).toBeNull();
  });

  test("a non-null value still writes its contract", () => {
    expect(JsonSerializer.serialize(new Cls(3), ctx.cls)).toEqual({ X: 3 });
  });
});
