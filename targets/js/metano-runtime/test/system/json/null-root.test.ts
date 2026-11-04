import { describe, expect, test } from "bun:test";
import { Decimal } from "decimal.js";
import {
  type JsonConverter,
  JsonSerializer,
  type JsonTypeInfo,
  SerializerContext,
  type TypeSpec,
  type ValueSpec,
} from "#/system/json/index.ts";
import { cannotConvertMessage, expectJsonException } from "./json-exception-assertions.ts";

// JSON `null`, as measured on System.Text.Json (.NET 10, source generated
// context, FR-037): `null` for every reference type and for `Nullable<T>`;
// "The JSON value could not be converted to <Type>." for every value type, at
// the root and for a struct property, element or dictionary value; and the
// empty text has no JSON token at all.

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

class Holder {
  constructor(
    readonly point: SPoint,
    readonly maybe: SPoint | null,
  ) {}
}

const NO_TOKENS_MESSAGE =
  "The input does not contain any JSON tokens. Expected the input to start with a valid JSON " +
  "token, when isFinalBlock is true.";

/** Mirrors a generated context: value-type specs carry `valueType` and `typeName`. */
class NullRootContext extends SerializerContext {
  private _cls?: TypeSpec<Cls>;
  private _sPoint?: TypeSpec<SPoint>;
  private _holder?: TypeSpec<Holder>;
  private _clip?: TypeSpec<Clip>;
  private _imageClip?: TypeSpec<ImageClip>;
  private _storedNote?: TypeSpec<StoredNote>;
  private _int32?: ValueSpec<number>;
  private _nullableInt32?: ValueSpec<number | null>;
  private _string?: ValueSpec<string | null>;
  private _listInt32?: ValueSpec<number[] | null>;
  private _dictionaryStringInt32?: ValueSpec<Map<string, number> | null>;
  private _listSPoint?: ValueSpec<SPoint[] | null>;
  private _listNullableSPoint?: ValueSpec<(SPoint | null)[] | null>;
  private _dictionaryStringSPoint?: ValueSpec<Map<string, SPoint> | null>;
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
      typeName: "Probe.SPoint",
    }));
  }

  get holder(): TypeSpec<Holder> {
    return (this._holder ??= this.createSpec({
      type: Holder,
      factory: (p) => new Holder(p.point as SPoint, p.maybe as SPoint | null),
      properties: [
        { ts: "point", json: "P", type: { kind: "ref", spec: () => this.sPoint } },
        {
          ts: "maybe",
          json: "M",
          type: { kind: "nullable", inner: { kind: "ref", spec: () => this.sPoint } },
          optional: true,
        },
      ],
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
      typeName: "System.Int32",
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

  get listSPoint(): ValueSpec<SPoint[] | null> {
    return (this._listSPoint ??= this.createValueSpec({
      descriptor: { kind: "array", element: { kind: "ref", spec: () => this.sPoint } },
    }));
  }

  get listNullableSPoint(): ValueSpec<(SPoint | null)[] | null> {
    return (this._listNullableSPoint ??= this.createValueSpec({
      descriptor: {
        kind: "array",
        element: { kind: "nullable", inner: { kind: "ref", spec: () => this.sPoint } },
      },
    }));
  }

  get dictionaryStringSPoint(): ValueSpec<Map<string, SPoint> | null> {
    return (this._dictionaryStringSPoint ??= this.createValueSpec({
      descriptor: {
        kind: "map",
        key: { kind: "primitive" },
        value: { kind: "ref", spec: () => this.sPoint },
      },
    }));
  }

  get decimal(): ValueSpec<Decimal> {
    return (this._decimal ??= this.createValueSpec({
      descriptor: { kind: "decimal", type: Decimal },
      valueType: true,
      typeName: "System.Decimal",
    }));
  }

  get level(): ValueSpec<Level> {
    return (this._level ??= this.createValueSpec({
      descriptor: { kind: "numericEnum", values: Level },
      valueType: true,
      typeName: "Probe.Level",
    }));
  }

  get episodeId(): ValueSpec<EpisodeId> {
    return (this._episodeId ??= this.createValueSpec({
      descriptor: { kind: "branded", create: EpisodeId.create },
      valueType: true,
      typeName: "Probe.EpisodeId",
    }));
  }
}

const ctx = new NullRootContext();

type Row<S> = readonly [name: string, spec: S];

/** Reference-type object contracts: JSON `null` reads, and `null` writes, as `null`. */
const OBJECT_ROOTS: readonly Row<TypeSpec<unknown>>[] = [
  ["class contract", ctx.cls],
  ["abstract polymorphic base", ctx.clip],
  ["[PlainObject] contract", ctx.storedNote],
];

/** Value roots whose `T` holds `| null`: reference types and `Nullable<T>`. */
const NULLABLE_VALUE_ROOTS: readonly Row<ValueSpec<unknown>>[] = [
  ["Nullable<int>", ctx.nullableInt32],
  ["string", ctx.string],
  ["List<int>", ctx.listInt32],
  ["Dictionary<string, int>", ctx.dictionaryStringInt32],
];

/** Every read entry point, static and bound, tree and text, returns `null`. */
function expectNullAtEveryEntryPoint(spec: TypeSpec<unknown>): void;
function expectNullAtEveryEntryPoint(spec: ValueSpec<unknown>): void;
function expectNullAtEveryEntryPoint(spec: JsonTypeInfo<unknown>): void {
  // The entry points dispatch on the spec's kind at runtime; the overloads
  // above keep each caller's spec kind checked.
  const anyKind = spec as TypeSpec<unknown>;
  const bound = JsonSerializer.withContext(ctx);

  expect(JsonSerializer.deserialize(null, anyKind)).toBeNull();
  expect(JsonSerializer.deserializeText(" null ", anyKind)).toBeNull();
  expect(bound.deserialize(null, anyKind)).toBeNull();
  expect(bound.deserializeText("null", anyKind)).toBeNull();
}

// ─── Reference-type roots ────────────────────────────────────────────────────

describe("JSON null at a reference-type root reads as null", () => {
  test.each(OBJECT_ROOTS)("%s", (_, spec) => {
    expectNullAtEveryEntryPoint(spec);
  });

  test.each(NULLABLE_VALUE_ROOTS)("%s value root", (_, spec) => {
    expectNullAtEveryEntryPoint(spec);
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
  test("a struct contract names its CLR type on every entry point", () => {
    const bound = JsonSerializer.withContext(ctx);
    const message = cannotConvertMessage("Probe.SPoint");

    expectJsonException(() => JsonSerializer.deserialize(null, ctx.sPoint), message);
    expectJsonException(() => JsonSerializer.deserializeText("null", ctx.sPoint), message);
    expectJsonException(() => bound.deserialize(null, ctx.sPoint), message);
    expectJsonException(() => bound.deserializeText("null", ctx.sPoint), message);
  });

  test("a struct contract names its CLR type for a non-object input too", () => {
    expectJsonException(
      () => JsonSerializer.deserialize(1, ctx.sPoint),
      cannotConvertMessage("Probe.SPoint"),
    );
  });

  test("a struct contract still reads an object", () => {
    expect(JsonSerializer.deserialize({ X: 1, Y: 2 }, ctx.sPoint)).toEqual(new SPoint(1, 2));
  });

  const namedValueTypes: readonly (readonly [string, ValueSpec<unknown>, string])[] = [
    ["int", ctx.int32, "System.Int32"],
    ["decimal", ctx.decimal, "System.Decimal"],
    ["numeric enum", ctx.level, "Probe.Level"],
    ["branded type", ctx.episodeId, "Probe.EpisodeId"],
  ];

  test.each(namedValueTypes)("a %s names its typeName on both read paths", (_, spec, name) => {
    const bound = JsonSerializer.withContext(ctx);

    expectJsonException(() => JsonSerializer.deserialize(null, spec), cannotConvertMessage(name));
    expectJsonException(() => bound.deserializeText("null", spec), cannotConvertMessage(name));
  });

  test("a spec without typeName names its constructor, or Object for a value root", () => {
    const point: TypeSpec<SPoint> = { ...ctx.sPoint, typeName: undefined };
    const int32: ValueSpec<number> = { descriptor: { kind: "primitive" }, valueType: true };

    expectJsonException(() => JsonSerializer.deserialize(null, point), cannotConvertMessage("SPoint"));
    expectJsonException(() => JsonSerializer.deserialize(null, int32), cannotConvertMessage("Object"));
  });

  test("the check runs before a custom converter of the root's kind", () => {
    const converters: JsonConverter[] = [
      { kind: "primitive", serialize: (value) => value, deserialize: () => 0 },
    ];

    expectJsonException(
      () => JsonSerializer.deserialize(null, ctx.int32, converters),
      cannotConvertMessage("System.Int32"),
    );
  });
});

// ─── Struct contracts below the root ─────────────────────────────────────────

describe("JSON null for a struct contract below the root", () => {
  const message = cannotConvertMessage("Probe.SPoint");

  test("a non-nullable struct property throws", () => {
    expectJsonException(() => JsonSerializer.deserialize({ P: null, M: null }, ctx.holder), message);
  });

  test("a Nullable<struct> property reads null", () => {
    const holder = JsonSerializer.deserialize({ P: { X: 1, Y: 2 }, M: null }, ctx.holder);

    expect(holder).toEqual(new Holder(new SPoint(1, 2), null));
  });

  test("a struct element of a list throws", () => {
    expectJsonException(
      () => JsonSerializer.deserialize([{ X: 1, Y: 2 }, null], ctx.listSPoint),
      message,
    );
  });

  test("a Nullable<struct> element reads null", () => {
    expect(JsonSerializer.deserialize([null], ctx.listNullableSPoint)).toEqual([null]);
  });

  test("a struct dictionary value throws", () => {
    expectJsonException(
      () => JsonSerializer.deserializeText('{"a":null}', ctx.dictionaryStringSPoint),
      message,
    );
  });

  test("a null struct property still writes null", () => {
    const holder = { point: null, maybe: null } as unknown as Holder;

    expect(JsonSerializer.serialize(holder, ctx.holder)).toEqual({ P: null, M: null });
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
  test.each(OBJECT_ROOTS)("%s", (_, spec) => {
    expect(JsonSerializer.serialize(null, spec)).toBeNull();
    expect(JsonSerializer.withContext(ctx).serialize(null, spec)).toBeNull();
  });

  test.each(NULLABLE_VALUE_ROOTS)("%s value root", (_, spec) => {
    expect(JsonSerializer.serialize(null, spec)).toBeNull();
  });

  test("undefined writes null through a TypeSpec, as a ref property does", () => {
    const missing = undefined as unknown as Cls;

    expect(JsonSerializer.serialize(missing, ctx.cls)).toBeNull();
  });

  test("a non-null value still writes its contract", () => {
    expect(JsonSerializer.serialize(new Cls(3), ctx.cls)).toEqual({ X: 3 });
  });
});
