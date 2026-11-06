import { describe, expect, test } from "bun:test";
import {
  type JsonReadOptions,
  JsonSerializer,
  SerializerContext,
  type TypeSpec,
  type ValueSpec,
} from "#/system/json/index.ts";
import {
  cannotConvertMessage,
  expectJsonException,
  expectNotSupportedException,
  METADATA_NOT_FIRST_MESSAGE,
  missingDiscriminatorMessage,
} from "./json-exception-assertions.ts";
import { abstractFactory } from "./spec-fixtures.ts";

/**
 * Context read options (spec 009 FR-002, FR-048 items 2–3). Measured with
 * source-generated contexts (.NET SDK 10.0.401, runtime 10.0.12):
 *
 * - Defaults: `General` → `PropertyNameCaseInsensitive = false`,
 *   `NumberHandling = Strict`, `AllowOutOfOrderMetadataProperties = false`;
 *   `Web` → `true`, `AllowReadingFromString`, `false`. A named option
 *   overrides the Web default (`PropertyNameCaseInsensitive = false`,
 *   `NumberHandling = Strict`).
 * - Web `ImageClip(Path, Seconds)`: `{"PATH":"a","seconds":1}` and
 *   `{"pAtH":"a","SECONDS":1}` read `Path = "a"`; `{"path":"a","Path":"b",…}`
 *   reads `"b"` and `{"Path":"b","path":"a",…}` reads `"a"` (document order,
 *   the last match wins). With `PropertyNameCaseInsensitive = false`,
 *   `{"Path":"a",…}` leaves `Path` unset.
 * - Web `Clip` (`kind` discriminator): `{"Kind":"image",…}` → NotSupportedException
 *   "… must specify a type discriminator." (the tag stays case-sensitive);
 *   `{"kind":"image","PATH":"a","Seconds":1}` reads `ImageClip("a", 1)`;
 *   `{"kind":"image","path":"a","seconds":"2.5"}` reads `Seconds = 2.5`.
 * - `PropertyNameCaseInsensitive = true`, `kind` discriminator, `KindDer(string
 *   Kind)`: `{"kind":"k","Kind":"x"}` reads `Kind = "x"`, `{"kind":"k"}`
 *   leaves `Kind` unset (the tag is metadata, never a property), and
 *   `{"Kind":"x","kind":"k"}` throws the "metadata property … not the first
 *   property" JsonException; read directly, `KindDer` takes `{"KIND":"x"}`.
 * - OrdinalIgnoreCase: `ÉCOLE` matches `école`, `ςigma` matches `Σigma`;
 *   `STRASSE` does not match `Straße`, `ıd` / `İD` do not match `Id`, `ſize`
 *   does not match `Size`, `Kelvin-K ey` does not match `Key`.
 * - `Web` + `AllowOutOfOrderMetadataProperties = true`:
 *   `{"path":"a","kind":"image","seconds":1}` reads `ImageClip`; without the
 *   option it is a missing tag.
 */

// ─── Test Domain ─────────────────────────────────────────────────────────────

abstract class Clip {
  constructor(readonly seconds: number) {}
}

class ImageClip extends Clip {
  constructor(
    readonly path: string,
    seconds: number,
  ) {
    super(seconds);
  }
}

class KindBase {}

class KindDer extends KindBase {
  constructor(readonly kind: string | null) {
    super();
  }
}

class Unicode {
  constructor(readonly values: Record<string, unknown>) {}
}

const WEB: JsonReadOptions = {
  propertyNameCaseInsensitive: true,
  numberHandling: "allowReadingFromString",
};

/** Mirrors the generated Web context of `json-web-context.ts`. */
class WebContext extends SerializerContext {
  private static readonly _default: WebContext = new WebContext();

  private _clip?: TypeSpec<Clip>;

  private _imageClip?: TypeSpec<ImageClip>;

  private _counts?: ValueSpec<number[] | null>;

  static get default(): WebContext {
    return WebContext._default;
  }

  protected override get readOptions(): JsonReadOptions {
    return WEB;
  }

  get clip(): TypeSpec<Clip> {
    return (this._clip ??= this.createSpec({
      type: Clip,
      factory: abstractFactory,
      properties: [
        { ts: "seconds", json: "seconds", type: { kind: "primitive", numberType: "System.Double" } },
      ],
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
      factory: (p) => new ImageClip(p.path as string, p.seconds as number),
      properties: [{ ts: "path", json: "path", type: { kind: "primitive" } }],
    }));
  }

  get counts(): ValueSpec<number[] | null> {
    return (this._counts ??= this.createValueSpec({
      descriptor: {
        kind: "array",
        element: { kind: "primitive", numberType: "System.Int32" },
      },
    }));
  }
}

/** A context with System.Text.Json's default options: no override. */
class DefaultContext extends SerializerContext {
  get imageClip(): TypeSpec<ImageClip> {
    return this.createSpec({
      type: ImageClip,
      factory: (p) => new ImageClip(p.path as string, p.seconds as number),
      properties: [
        { ts: "path", json: "path", type: { kind: "primitive" } },
        { ts: "seconds", json: "seconds", type: { kind: "primitive" } },
      ],
    });
  }
}

function read<T>(text: string, spec: TypeSpec<T>): T | null {
  return JsonSerializer.deserialize(JSON.parse(text), spec);
}

// ─── Context attachment ──────────────────────────────────────────────────────

describe("read options — context attachment", () => {
  test("createSpec and createValueSpec attach the context's read options", () => {
    const ctx = WebContext.default;

    expect(ctx.imageClip.readOptions).toEqual(WEB);
    expect(ctx.clip.readOptions).toEqual(WEB);
    expect(ctx.counts.readOptions).toEqual(WEB);
  });

  test("the registry resolves the spec with the options attached", () => {
    const ctx = WebContext.default;

    expect(ctx.resolve(ImageClip)).toBe(ctx.imageClip);
  });

  test("a context with default options leaves its specs unchanged", () => {
    expect("readOptions" in new DefaultContext().imageClip).toBe(false);
  });

  test("an option the spec declares wins over the context's", () => {
    class Overriding extends WebContext {
      get strictClip(): TypeSpec<ImageClip> {
        return this.createSpec({
          ...this.imageClip,
          readOptions: { propertyNameCaseInsensitive: false },
        });
      }
    }

    expect(new Overriding().strictClip.readOptions).toEqual({
      propertyNameCaseInsensitive: false,
      numberHandling: "allowReadingFromString",
    });
  });

  test("the static JsonSerializer sees the attached options", () => {
    const value = read('{"kind":"image","PATH":"a","Seconds":"1"}', WebContext.default.clip);

    expect(value).toEqual(new ImageClip("a", 1));
    expect(JsonSerializer.deserialize(["1", 2], WebContext.default.counts)).toEqual([1, 2]);
  });
});

// ─── Case-insensitive property names ─────────────────────────────────────────

describe("read options — case-insensitive property names", () => {
  const imageClip = WebContext.default.imageClip;

  test("keys match properties in any letter case", () => {
    expect(read('{"PATH":"a","seconds":1}', imageClip)).toEqual(new ImageClip("a", 1));
    expect(read('{"pAtH":"a","SECONDS":1}', imageClip)).toEqual(new ImageClip("a", 1));
  });

  test("two keys that match one property: the last one in the document wins", () => {
    expect(read('{"path":"a","Path":"b","seconds":1}', imageClip)?.path).toBe("b");
    expect(read('{"Path":"b","path":"a","seconds":1}', imageClip)?.path).toBe("a");
  });

  test("a property no key matches is still missing", () => {
    expectJsonException(
      () => read('{"pathx":"a","seconds":1}', imageClip),
      'Missing required property: "path"',
    );
  });

  test("without the option, names stay case-sensitive", () => {
    expectJsonException(
      () => read('{"PATH":"a","seconds":1}', new DefaultContext().imageClip),
      'Missing required property: "path"',
    );
  });

  test("an inherited property matches ignoring case too", () => {
    expect(read('{"path":"a","SeCoNdS":2}', imageClip)?.seconds).toBe(2);
  });

  test("names compare like OrdinalIgnoreCase, not like toUpperCase", () => {
    const spec: TypeSpec<Unicode> = {
      type: Unicode,
      factory: (p) => new Unicode(p),
      properties: ["Straße", "Id", "Size", "Key", "École", "Σigma", "ᾳ"].map((name) => ({
        ts: name,
        json: name,
        type: { kind: "primitive" },
        optional: true,
      })),
      readOptions: { propertyNameCaseInsensitive: true },
    };

    const values = (text: string) => read(text, spec)?.values;

    expect(values('{"STRAßE":1}')?.Straße).toBe(1);
    expect(values('{"STRASSE":1}')?.Straße).toBeNull();
    expect(values('{"ıd":2}')?.Id).toBeNull();
    expect(values('{"İD":2}')?.Id).toBeNull();
    expect(values('{"ſize":3}')?.Size).toBeNull();
    expect(values('{"Key":4}')?.Key).toBeNull();
    expect(values('{"ÉCOLE":5}')?.École).toBe(5);
    expect(values('{"ςIGMA":6}')?.Σigma).toBe(6);
    expect(values('{"ᾼ":7}')?.ᾳ).toBe(7);
  });
});

// ─── Polymorphism ────────────────────────────────────────────────────────────

describe("read options — polymorphic payloads", () => {
  const clip = WebContext.default.clip;

  test("the derived contract reads keys ignoring case and numbers from strings", () => {
    expect(read('{"kind":"image","PATH":"a","Seconds":1}', clip)).toEqual(new ImageClip("a", 1));
    expect(read('{"kind":"image","path":"a","seconds":"2.5"}', clip)).toEqual(
      new ImageClip("a", 2.5),
    );
  });

  test("the discriminator stays case-sensitive", () => {
    expectNotSupportedException(
      () => read('{"Kind":"image","path":"a","seconds":1}', clip),
      missingDiscriminatorMessage("Clip"),
    );
  });

  test("a number that fails the grammar names the declaring contract", () => {
    expectJsonException(
      () => read('{"kind":"image","path":"a","seconds":"x"}', clip),
      cannotConvertMessage("ImageClip"),
    );
  });

  describe("a property named like the discriminator ignoring case", () => {
    const options: JsonReadOptions = { propertyNameCaseInsensitive: true };

    const kindDer: TypeSpec<KindDer> = {
      type: KindDer,
      factory: (p) => new KindDer(p.kind as string | null),
      properties: [{ ts: "kind", json: "Kind", type: { kind: "primitive" }, optional: true }],
      readOptions: options,
    };

    const kindBase: TypeSpec<KindBase> = {
      type: KindBase,
      factory: () => new KindBase(),
      properties: [],
      polymorphism: { discriminator: "kind", derived: [{ tag: "k", spec: () => kindDer }] },
      readOptions: options,
    };

    test("the leading tag is metadata, never the property", () => {
      expect(read('{"kind":"k","Kind":"x"}', kindBase)).toEqual(new KindDer("x"));
      expect(read('{"kind":"k"}', kindBase)).toEqual(new KindDer(null));
    });

    test("a data key first leaves the later tag misplaced", () => {
      expectJsonException(() => read('{"Kind":"x","kind":"k"}', kindBase), METADATA_NOT_FIRST_MESSAGE);
    });

    // System.Text.Json rejects a property named exactly like the discriminator
    // ("The type 'KDer' contains property 'kind' that conflicts with an existing
    // metadata property name.", measured; MS0032 in generated contexts). A
    // hand-written spec gets the same rule on both lookups: the tag is metadata.
    test("a property named exactly like the discriminator is never filled by the tag", () => {
      const exactDer: TypeSpec<KindDer> = {
        type: KindDer,
        factory: (p) => new KindDer(p.kind as string | null),
        properties: [{ ts: "kind", json: "kind", type: { kind: "primitive" }, optional: true }],
      };

      const exactBase: TypeSpec<KindBase> = {
        type: KindBase,
        factory: () => new KindBase(),
        properties: [],
        polymorphism: { discriminator: "kind", derived: [{ tag: "k", spec: () => exactDer }] },
      };

      expect(read('{"kind":"k"}', exactBase)).toEqual(new KindDer(null));
      expect(read('{"kind":"x"}', exactDer)).toEqual(new KindDer("x"));
    });

    test("read directly, the contract matches the key ignoring case", () => {
      expect(read('{"KIND":"x"}', kindDer)).toEqual(new KindDer("x"));
      expect(read('{"kind":"x"}', kindDer)).toEqual(new KindDer("x"));
    });
  });

  describe("out-of-order metadata from the context (FR-002)", () => {
    class OutOfOrderContext extends WebContext {
      protected override get readOptions(): JsonReadOptions {
        return { ...WEB, allowOutOfOrderMetadataProperties: true };
      }
    }

    test("the tag may follow other properties", () => {
      expect(read('{"path":"a","kind":"image","seconds":1}', new OutOfOrderContext().clip)).toEqual(
        new ImageClip("a", 1),
      );
    });

    test("without the option it is a missing tag", () => {
      expectNotSupportedException(
        () => read('{"path":"a","kind":"image","seconds":1}', clip),
        missingDiscriminatorMessage("Clip"),
      );
    });
  });
});
