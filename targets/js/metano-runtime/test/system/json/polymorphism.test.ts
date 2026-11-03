import { describe, expect, test } from "bun:test";
import {
  JsonSerializer,
  SerializerContext,
  type JsonConverter,
  type PolymorphismSpec,
  type TypeSpec,
  type ValueSpec,
} from "#/system/json/index.ts";
import {
  cannotConvertMessage,
  DOLLAR_PROPERTY_MESSAGE,
  expectJsonException,
  expectNotSupportedException,
  LEADING_VALUES_MESSAGE,
  METADATA_NOT_FIRST_MESSAGE,
  metadataTokenMessage,
  missingDiscriminatorMessage,
  outOfOrderValuesMessage,
  unrecognizedDiscriminatorMessage,
  unsupportedRuntimeTypeMessage,
} from "./json-exception-assertions.ts";
import { abstractFactory } from "./spec-fixtures.ts";

// Hand-written specs shaped like the generated context of contracts/context-codegen.md §1.
// Expected text mirrors System.Text.Json source-generated contexts on .NET 10 (research.md R1,
// R8, and an S5 probe):
//   Clip [JsonPolymorphic("kind")] image/silence   ImageClip("a.png",1.5) → {"kind":"image","Path":"a.png","Seconds":1.5}
//   {"kind":"video",…} / {"kind":"Image",…}        → JsonException "Read unrecognized type discriminator id 'video'."
//   {"Seconds":2} / tag not first (abstract base)  → NotSupportedException "… must specify a type discriminator."
//   tag not first on a concrete base, a later tag, $id / $ref
//                                                  → JsonException "The metadata property is either not supported …"
//   any other key starting with '$' (after the tag, or on a concrete base)
//                                                  → JsonException "Properties that start with '$' are not allowed …"
//   {"$type":"d","0":5,…}                          → reads CDer (an index key after the tag is fine)
//   tag not first + AllowOutOfOrderMetadataProperties → reads the derived type
//   IBase int tag 1 / string tag "s"               IA(5) → {"$type":1,"X":5}; {"$type":"1"} unrecognized '1'
//   {"$type":2} / {"$type":-1}                     → unrecognized id '2' / '-1'
//   {"$type":1.5} / {"$type":3000000000}           → JsonException "The JSON value could not be converted to IBase."
//   {"t":1} on a base with string tags only         → unrecognized id '1'
//   Self-registered SBase "b"                       SBase(1) → {"$type":"b","V":1}, reads back as SBase
//   FallBackToNearestAncestor, NSelfZ : NSelf       → {"$type":"b","V":1} (nearest = self-registered base)
//   FallBackToNearestAncestor, UNearB : UNearA "a"  → {"$type":"a","B":2,"A":1}
//   FallBackToNearestAncestor, none registered      → base contract, no tag ({"V":1})
//   FallBackToBaseType, UBaseX : UBaseH (abstract)  → {"A":1}
//   Untagged UA : UBase                             → {"A":2,"V":1}; {"V":1,"A":2} reads as UBase
//   IgnoreUnrecognizedTypeDiscriminators            concrete base → base; abstract → NotSupportedException
//   Nested NMid : NBase (both polymorphic)          → outer tag only; NLeaf (on NMid only) fails via NBase
//   Interface IShape                               Sq(2) → {"$type":"sq","Side":2}; Other → NotSupportedException

const primitive = { kind: "primitive" } as const;

// ─── Clip hierarchy (string tags, custom discriminator) ──────────────────────

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

class SilenceClip extends Clip {}

class VideoClip extends Clip {}

class Group extends Clip {
  constructor(
    readonly clips: Clip[],
    seconds: number,
  ) {
    super(seconds);
  }
}

class Timeline {
  constructor(
    readonly name: string,
    readonly main: Clip,
    readonly clips: Clip[],
  ) {}
}

class StudioContext extends SerializerContext {
  private _clip?: TypeSpec<Clip>;

  private _imageClip?: TypeSpec<ImageClip>;

  private _silenceClip?: TypeSpec<SilenceClip>;

  private _group?: TypeSpec<Group>;

  private _timeline?: TypeSpec<Timeline>;

  private _listClip?: ValueSpec<Clip[] | null>;

  private _dictionaryStringClip?: ValueSpec<Map<string, Clip> | null>;

  get clip(): TypeSpec<Clip> {
    return (this._clip ??= this.createSpec<Clip>({
      type: Clip,
      factory: () => {
        throw new Error(
          "Cannot deserialize abstract type 'Clip' directly; deserialize it through a polymorphic spec.",
        );
      },
      properties: [{ ts: "seconds", json: "Seconds", type: primitive }],
      polymorphism: {
        discriminator: "kind",
        abstract: true,
        derived: [
          { tag: "image", spec: () => this.imageClip },
          { tag: "silence", spec: () => this.silenceClip },
          { tag: "group", spec: () => this.group },
        ],
      },
    }));
  }

  get imageClip(): TypeSpec<ImageClip> {
    return (this._imageClip ??= this.createSpec({
      type: ImageClip,
      base: this.clip,
      factory: (p) => new ImageClip(p.path as string, p.seconds as number),
      properties: [{ ts: "path", json: "Path", type: primitive }],
    }));
  }

  get silenceClip(): TypeSpec<SilenceClip> {
    return (this._silenceClip ??= this.createSpec({
      type: SilenceClip,
      base: this.clip,
      factory: (p) => new SilenceClip(p.seconds as number),
      properties: [],
    }));
  }

  get group(): TypeSpec<Group> {
    return (this._group ??= this.createSpec({
      type: Group,
      base: this.clip,
      factory: (p) => new Group(p.clips as Clip[], p.seconds as number),
      properties: [
        {
          ts: "clips",
          json: "Clips",
          type: { kind: "array", element: { kind: "ref", spec: () => this.clip } },
        },
      ],
    }));
  }

  get timeline(): TypeSpec<Timeline> {
    return (this._timeline ??= this.createSpec({
      type: Timeline,
      factory: (p) => new Timeline(p.name as string, p.main as Clip, p.clips as Clip[]),
      properties: [
        { ts: "name", json: "Name", type: primitive },
        { ts: "main", json: "Main", type: { kind: "ref", spec: () => this.clip } },
        {
          ts: "clips",
          json: "Clips",
          type: { kind: "array", element: { kind: "ref", spec: () => this.clip } },
        },
      ],
    }));
  }

  get listClip(): ValueSpec<Clip[] | null> {
    return (this._listClip ??= this.createValueSpec({
      descriptor: { kind: "array", element: { kind: "ref", spec: () => this.clip } },
    }));
  }

  get dictionaryStringClip(): ValueSpec<Map<string, Clip> | null> {
    return (this._dictionaryStringClip ??= this.createValueSpec({
      descriptor: {
        kind: "map",
        key: primitive,
        value: { kind: "ref", spec: () => this.clip },
      },
    }));
  }
}

const studio = new StudioContext();

/** Serializes through `spec` and returns the exact JSON text. */
function write<T>(value: T, spec: TypeSpec<T>): string {
  return JSON.stringify(JsonSerializer.serialize(value, spec));
}

/** Parses `json` and deserializes it through `spec`. */
function read<T>(json: string, spec: TypeSpec<T>): T {
  return JsonSerializer.deserialize(JSON.parse(json), spec);
}

describe("polymorphism — string tags (Clip)", () => {
  test("writes the tag first, then derived properties before inherited ones (W1)", () => {
    expect(write(new ImageClip("a.png", 1.5), studio.clip)).toBe(
      '{"kind":"image","Path":"a.png","Seconds":1.5}',
    );
  });

  test("a derived type with no own properties writes the tag and the base's (W2)", () => {
    expect(write(new SilenceClip(2), studio.clip)).toBe('{"kind":"silence","Seconds":2}');
  });

  test("the derived spec itself writes no tag (W3)", () => {
    expect(write(new ImageClip("a.png", 1.5), studio.imageClip)).toBe(
      '{"Path":"a.png","Seconds":1.5}',
    );
  });

  test("reads the derived class the tag names, never calling the abstract factory", () => {
    const clip = read('{"kind":"silence","Seconds":2}', studio.clip);

    expect(clip).toBeInstanceOf(SilenceClip);
    expect(clip.seconds).toBe(2);
  });

  test("round-trips an image clip", () => {
    const clip = read(write(new ImageClip("a.png", 1.5), studio.clip), studio.clip);

    expect(clip).toBeInstanceOf(ImageClip);
    expect(clip).toEqual(new ImageClip("a.png", 1.5));
  });

  test("an unknown tag throws JsonException (E1)", () => {
    expectJsonException(
      () => read('{"kind":"video","Seconds":2}', studio.clip),
      unrecognizedDiscriminatorMessage("video"),
    );
  });

  test("tags match case-sensitively (E2)", () => {
    expectJsonException(
      () => read('{"kind":"Image","Seconds":2}', studio.clip),
      unrecognizedDiscriminatorMessage("Image"),
    );
  });

  test("a missing tag on an abstract base throws NotSupportedException (E3)", () => {
    expectNotSupportedException(
      () => read('{"Seconds":2}', studio.clip),
      missingDiscriminatorMessage("Clip"),
    );
  });

  test("a tag that is not the first key is a missing tag by default (E4)", () => {
    expectNotSupportedException(
      () => read('{"Path":"a","kind":"image","Seconds":1}', studio.clip),
      missingDiscriminatorMessage("Clip"),
    );
  });

  test("the discriminator name matches case-sensitively", () => {
    expectNotSupportedException(
      () => read('{"Kind":"image","Path":"a","Seconds":1}', studio.clip),
      missingDiscriminatorMessage("Clip"),
    );
  });

  test("reading through the derived spec ignores the tag as an unknown property", () => {
    const clip = read('{"kind":"image","Path":"a","Seconds":1}', studio.imageClip);

    expect(clip).toEqual(new ImageClip("a", 1));
  });

  test("an unregistered runtime type fails by default", () => {
    expectNotSupportedException(
      () => write(new VideoClip(1), studio.clip),
      unsupportedRuntimeTypeMessage("VideoClip", "Clip"),
    );
  });

  test("a non-object payload names the base", () => {
    expectJsonException(() => read("[1]", studio.clip), cannotConvertMessage("Clip"));

    expectJsonException(() => read('"x"', studio.clip), cannotConvertMessage("Clip"));
  });
});

describe("polymorphism — non-scalar tags", () => {
  const cases: readonly (readonly [string, string])[] = [
    ["null", "Null"],
    ["true", "True"],
    ["false", "False"],
    ["{}", "StartObject"],
    ["[]", "StartArray"],
  ];

  for (const [tag, token] of cases) {
    test(`a ${tag} tag names the ${token} token (E5)`, () => {
      expectJsonException(
        () => read(`{"kind":${tag},"Seconds":2}`, studio.clip),
        metadataTokenMessage(token),
      );
    });
  }
});

describe("polymorphism — dispatch through containers", () => {
  test("a ref property and an array of the base carry each value's tag (W4)", () => {
    const timeline = new Timeline("t", new ImageClip("a.png", 1.5), [new SilenceClip(2)]);
    const json = write(timeline, studio.timeline);

    expect(json).toBe(
      '{"Name":"t","Main":{"kind":"image","Path":"a.png","Seconds":1.5},"Clips":[{"kind":"silence","Seconds":2}]}',
    );

    const back = read(json, studio.timeline);

    expect(back.main).toBeInstanceOf(ImageClip);
    expect(back.clips[0]).toBeInstanceOf(SilenceClip);
  });

  test("an array root of the base dispatches every element (W5)", () => {
    const json = JSON.stringify(
      JsonSerializer.serialize([new ImageClip("a", 1.5), new SilenceClip(2)], studio.listClip),
    );

    expect(json).toBe('[{"kind":"image","Path":"a","Seconds":1.5},{"kind":"silence","Seconds":2}]');

    const back = JsonSerializer.deserialize(JSON.parse(json), studio.listClip);

    expect(back?.[0]).toBeInstanceOf(ImageClip);
    expect(back?.[1]).toBeInstanceOf(SilenceClip);
  });

  test("a dictionary root of the base dispatches every value (W6)", () => {
    const map = new Map<string, Clip>([["x", new SilenceClip(2)]]);
    const json = JSON.stringify(JsonSerializer.serialize(map, studio.dictionaryStringClip));

    expect(json).toBe('{"x":{"kind":"silence","Seconds":2}}');

    const back = JsonSerializer.deserialize(JSON.parse(json), studio.dictionaryStringClip);

    expect(back?.get("x")).toBeInstanceOf(SilenceClip);
  });

  test("an iterable of the base dispatches every element (W23)", () => {
    const iterableClip: ValueSpec<Iterable<Clip>> = { descriptor: studio.listClip.descriptor };
    const clips = new Set<Clip>([new SilenceClip(2)]);

    expect(JSON.stringify(JsonSerializer.serialize(clips, iterableClip))).toBe(
      '[{"kind":"silence","Seconds":2}]',
    );
  });

  test("a self-containing value throws the object-cycle JsonException (E12)", () => {
    const group = new Group([], 1);
    group.clips.push(group);

    expectJsonException(
      () => write(group, studio.clip),
      "A possible object cycle was detected. This can either be due to a cycle or if the object " +
        "depth is larger than the maximum allowed depth of 64. Consider using " +
        "ReferenceHandler.Preserve on JsonSerializerOptions to support cycles.",
    );
  });

  test("a custom converter for kind ref replaces dispatch (unchanged rule)", () => {
    const refAsTag: JsonConverter = {
      kind: "ref",
      serialize: (value) => (value as Clip).constructor.name,
      deserialize: (value) => value,
    };

    const json = JsonSerializer.serialize(
      new Timeline("t", new SilenceClip(1), []),
      studio.timeline,
      [refAsTag],
    );

    expect(json).toEqual({ Name: "t", Main: "SilenceClip", Clips: [] });
  });
});

describe("polymorphism — getter access order", () => {
  test("the derived getter may be read before the base getter", () => {
    const context = new StudioContext();
    const imageClip = context.imageClip;

    expect(imageClip.base).toBe(context.clip);
    expect(write(new ImageClip("a", 1), context.clip)).toBe(
      '{"kind":"image","Path":"a","Seconds":1}',
    );
  });

  test("the base getter may be read before any derived getter", () => {
    const context = new StudioContext();

    expect(read('{"kind":"image","Path":"a","Seconds":1}', context.clip)).toBeInstanceOf(ImageClip);
  });
});

describe("polymorphism — out-of-order metadata", () => {
  const clipAnywhere: TypeSpec<Clip> = {
    ...studio.clip,
    readOptions: { allowOutOfOrderMetadataProperties: true },
  };

  test("with the option, the tag may follow other properties", () => {
    const clip = read('{"Path":"a","kind":"image","Seconds":1}', clipAnywhere);

    expect(clip).toEqual(new ImageClip("a", 1));
    expect(clip).toBeInstanceOf(ImageClip);
  });

  test("with the option, a missing tag still throws NotSupportedException", () => {
    expectNotSupportedException(
      () => read('{"Seconds":1}', clipAnywhere),
      missingDiscriminatorMessage("Clip"),
    );
  });

  test("with the option, a later non-scalar tag is rejected by its token", () => {
    expectJsonException(
      () => read('{"Seconds":1,"kind":true}', clipAnywhere),
      metadataTokenMessage("True"),
    );
  });

  test("without the option, a later non-scalar tag is a missing tag", () => {
    expectNotSupportedException(
      () => read('{"Seconds":1,"kind":null}', studio.clip),
      missingDiscriminatorMessage("Clip"),
    );
  });
});

// ─── Int and mixed tags ($type) ──────────────────────────────────────────────

abstract class IBase {}

class IA extends IBase {
  constructor(readonly x: number) {
    super();
  }
}

class IS extends IBase {
  constructor(readonly y: number) {
    super();
  }
}

const iaSpec: TypeSpec<IA> = {
  type: IA,
  factory: (p) => new IA(p.x as number),
  properties: [{ ts: "x", json: "X", type: primitive }],
};

const isSpec: TypeSpec<IS> = {
  type: IS,
  factory: (p) => new IS(p.y as number),
  properties: [{ ts: "y", json: "Y", type: primitive }],
};

const iBaseSpec: TypeSpec<IBase> = {
  type: IBase,
  factory: abstractFactory,
  properties: [],
  polymorphism: {
    discriminator: "$type",
    abstract: true,
    derived: [
      { tag: 1, spec: () => iaSpec },
      { tag: "s", spec: () => isSpec },
    ],
  },
};

describe("polymorphism — int and mixed tags (IBase)", () => {
  test("an int tag is written as a JSON number under $type (W9)", () => {
    expect(write(new IA(5), iBaseSpec)).toBe('{"$type":1,"X":5}');
  });

  test("a string tag on the same base is written as a string (W10)", () => {
    expect(write(new IS(2), iBaseSpec)).toBe('{"$type":"s","Y":2}');
  });

  test("int and string tags round-trip", () => {
    expect(read('{"$type":1,"X":5}', iBaseSpec)).toEqual(new IA(5));
    expect(read('{"$type":"s","Y":2}', iBaseSpec)).toBeInstanceOf(IS);
  });

  test("a string tag never matches an int registration (E6)", () => {
    expectJsonException(
      () => read('{"$type":"1","X":5}', iBaseSpec),
      unrecognizedDiscriminatorMessage("1"),
    );
  });

  test("an unknown int tag reports its number", () => {
    expectJsonException(
      () => read('{"$type":2,"X":5}', iBaseSpec),
      unrecognizedDiscriminatorMessage("2"),
    );

    expectJsonException(
      () => read('{"$type":-1,"X":5}', iBaseSpec),
      unrecognizedDiscriminatorMessage("-1"),
    );
  });

  test("a number tag outside Int32 cannot be converted to the base", () => {
    expectJsonException(
      () => read('{"$type":1.5,"X":5}', iBaseSpec),
      cannotConvertMessage("IBase"),
    );

    expectJsonException(
      () => read('{"$type":3000000000,"X":5}', iBaseSpec),
      cannotConvertMessage("IBase"),
    );
  });

  test("a number tag on a base with string tags only is an unknown id", () => {
    expectJsonException(
      () => read('{"kind":1,"Seconds":2}', studio.clip),
      unrecognizedDiscriminatorMessage("1"),
    );
  });
});

// ─── Concrete bases ──────────────────────────────────────────────────────────

class DBase {}

class DDer extends DBase {
  constructor(readonly v: number) {
    super();
  }
}

const dDerSpec: TypeSpec<DDer> = {
  type: DDer,
  factory: (p) => new DDer(p.v as number),
  properties: [{ ts: "v", json: "V", type: primitive }],
};

const dBaseSpec: TypeSpec<DBase> = {
  type: DBase,
  factory: () => new DBase(),
  properties: [],
  polymorphism: { discriminator: "$type", derived: [{ tag: "d", spec: () => dDerSpec }] },
};

class SBase {
  constructor(readonly v: number) {}
}

class SDer extends SBase {
  constructor(
    v: number,
    readonly w: number,
  ) {
    super(v);
  }
}

class SOther extends SBase {}

const sDerSpec: TypeSpec<SDer> = {
  type: SDer,
  factory: (p) => new SDer(p.v as number, p.w as number),
  properties: [{ ts: "w", json: "W", type: primitive }],
};

const sBaseSpec: TypeSpec<SBase> = {
  type: SBase,
  factory: (p) => new SBase(p.v as number),
  properties: [{ ts: "v", json: "V", type: primitive }],
  polymorphism: {
    discriminator: "$type",
    unknownDerivedType: "fallBackToNearestAncestor",
    derived: [
      { tag: "b", spec: () => sBaseSpec },
      { tag: "x", spec: () => sDerSpec },
    ],
  },
};

const sDerLinked: TypeSpec<SDer> = { ...sDerSpec, base: sBaseSpec };

describe("polymorphism — concrete bases", () => {
  test("a base instance that is not registered writes no tag (W11)", () => {
    expect(write(new DBase(), dBaseSpec)).toBe("{}");
  });

  test("a missing tag reads the concrete base", () => {
    expect(read("{}", dBaseSpec)).toBeInstanceOf(DBase);
    expect(read('{"V":1}', dBaseSpec)).not.toBeInstanceOf(DDer);
  });

  test("a self-registered base writes its own tag (W11b)", () => {
    expect(write(new SBase(1), sBaseSpec)).toBe('{"$type":"b","V":1}');
  });

  test("a self-registered base tag reads back as the base", () => {
    const value = read('{"$type":"b","V":1}', sBaseSpec);

    expect(value).toBeInstanceOf(SBase);
    expect(value).toEqual(new SBase(1));
  });

  test("the nearest registered ancestor may be the self-registered base", () => {
    expect(write(new SOther(1), sBaseSpec)).toBe('{"$type":"b","V":1}');
  });

  test("a registered derived type linked to the base writes derived-first", () => {
    const linked: TypeSpec<SBase> = {
      ...sBaseSpec,
      polymorphism: {
        discriminator: "$type",
        derived: [{ tag: "x", spec: () => sDerLinked }],
      },
    };

    expect(write(new SDer(1, 2), linked)).toBe('{"$type":"x","W":2,"V":1}');
  });
});

// ─── Unknown derived types ───────────────────────────────────────────────────

abstract class UFail {}

class UFailA extends UFail {}

class UFailX extends UFail {
  constructor(readonly a: number) {
    super();
  }
}

const uFailSpec: TypeSpec<UFail> = {
  type: UFail,
  factory: abstractFactory,
  properties: [],
  polymorphism: {
    discriminator: "$type",
    abstract: true,
    derived: [
      {
        tag: "a",
        spec: () => ({
          type: UFailA,
          base: uFailSpec,
          factory: () => new UFailA(),
          properties: [],
        }),
      },
    ],
  },
};

abstract class UNear {
  constructor(readonly a: number) {}
}

class UNearA extends UNear {
  constructor(
    a: number,
    readonly b: number,
  ) {
    super(a);
  }
}

class UNearB extends UNearA {
  constructor(
    a: number,
    b: number,
    readonly c: number,
  ) {
    super(a, b);
  }
}

abstract class UNearAbstract extends UNear {}

class UNearC extends UNearAbstract {
  constructor(
    a: number,
    readonly d: number,
  ) {
    super(a);
  }
}

class UNearZ extends UNear {}

const uNearSpec: TypeSpec<UNear> = {
  type: UNear,
  factory: abstractFactory,
  properties: [{ ts: "a", json: "A", type: primitive }],
  polymorphism: {
    discriminator: "$type",
    abstract: true,
    unknownDerivedType: "fallBackToNearestAncestor",
    derived: [
      { tag: "a", spec: () => uNearASpec },
      { tag: "abs", spec: () => uNearAbstractSpec },
    ],
  },
};

const uNearASpec: TypeSpec<UNearA> = {
  type: UNearA,
  base: uNearSpec,
  factory: (p) => new UNearA(p.a as number, p.b as number),
  properties: [{ ts: "b", json: "B", type: primitive }],
};

const uNearAbstractSpec: TypeSpec<UNearAbstract> = {
  type: UNearAbstract,
  base: uNearSpec,
  factory: abstractFactory,
  properties: [],
};

abstract class UBaseH {
  constructor(readonly a: number) {}
}

class UBaseA extends UBaseH {}

class UBaseX extends UBaseH {
  constructor(
    a: number,
    readonly b: number,
  ) {
    super(a);
  }
}

const uBaseHSpec: TypeSpec<UBaseH> = {
  type: UBaseH,
  factory: abstractFactory,
  properties: [{ ts: "a", json: "A", type: primitive }],
  polymorphism: {
    discriminator: "$type",
    abstract: true,
    unknownDerivedType: "fallBackToBaseType",
    derived: [
      {
        tag: "a",
        spec: () => ({
          type: UBaseA,
          base: uBaseHSpec,
          factory: (p) => new UBaseA(p.a as number),
          properties: [],
        }),
      },
    ],
  },
};

describe("polymorphism — unknown derived type handling", () => {
  test("failSerialization throws NotSupportedException (E8)", () => {
    expectNotSupportedException(
      () => write(new UFailX(1), uFailSpec),
      unsupportedRuntimeTypeMessage("UFailX", "UFail"),
    );
  });

  test("fallBackToNearestAncestor writes the nearest registered ancestor (W12)", () => {
    const json = write(new UNearB(1, 2, 3), uNearSpec);

    expect(json).toBe('{"$type":"a","B":2,"A":1}');

    const value = read(json, uNearSpec);

    expect(value).toBeInstanceOf(UNearA);
    expect(value).not.toBeInstanceOf(UNearB);
  });

  test("fallBackToNearestAncestor accepts an abstract registration (FR-017)", () => {
    expect(write(new UNearC(1, 4), uNearSpec)).toBe('{"$type":"abs","A":1}');
  });

  test("fallBackToNearestAncestor with no registered ancestor writes the base, untagged", () => {
    expect(write(new UNearZ(1), uNearSpec)).toBe('{"A":1}');
  });

  test("fallBackToBaseType writes the base contract with no tag, also for an abstract base (W13)", () => {
    const json = write(new UBaseX(1, 2), uBaseHSpec);

    expect(json).toBe('{"A":1}');

    expectNotSupportedException(
      () => read(json, uBaseHSpec),
      missingDiscriminatorMessage("UBaseH"),
    );
  });

  test("a plain-object copy of a class variant is an unregistered runtime type", () => {
    expectNotSupportedException(
      () => write({ ...new SilenceClip(2) } as Clip, studio.clip),
      unsupportedRuntimeTypeMessage("Object", "Clip"),
    );
  });
});

// ─── Untagged registrations ──────────────────────────────────────────────────

class UBase {
  constructor(readonly v: number) {}
}

class UA extends UBase {
  constructor(
    v: number,
    readonly a: number,
  ) {
    super(v);
  }
}

const uBaseSpec: TypeSpec<UBase> = {
  type: UBase,
  factory: (p) => new UBase(p.v as number),
  properties: [{ ts: "v", json: "V", type: primitive }],
  polymorphism: {
    discriminator: "$type",
    derived: [
      {
        spec: () => ({
          type: UA,
          base: uBaseSpec,
          factory: (p) => new UA(p.v as number, p.a as number),
          properties: [{ ts: "a", json: "A", type: primitive }],
        }),
      },
    ],
  },
};

describe("polymorphism — untagged registrations", () => {
  test("an untagged derived type writes its contract without a tag", () => {
    expect(write(new UA(1, 2), uBaseSpec)).toBe('{"A":2,"V":1}');
  });

  test("its text reads back as the base (data lost, as in System.Text.Json)", () => {
    const value = read('{"A":2,"V":1}', uBaseSpec);

    expect(value).toBeInstanceOf(UBase);
    expect(value).not.toBeInstanceOf(UA);
  });
});

// ─── IgnoreUnrecognizedTypeDiscriminators ────────────────────────────────────

class IgnBase {
  constructor(readonly v: number) {}
}

class IgnA extends IgnBase {}

abstract class IgnAbs {}

class IgnAbsA extends IgnAbs {}

function ignorePolymorphism(spec: () => TypeSpec): PolymorphismSpec {
  return { discriminator: "k", ignoreUnrecognizedTags: true, derived: [{ tag: "a", spec }] };
}

const ignBaseSpec: TypeSpec<IgnBase> = {
  type: IgnBase,
  factory: (p) => new IgnBase(p.v as number),
  properties: [{ ts: "v", json: "V", type: primitive }],
  polymorphism: ignorePolymorphism(() => ({
    type: IgnA,
    base: ignBaseSpec,
    factory: (p) => new IgnA(p.v as number),
    properties: [],
  })),
};

const ignAbsSpec: TypeSpec<IgnAbs> = {
  type: IgnAbs,
  factory: abstractFactory,
  properties: [],
  polymorphism: {
    ...ignorePolymorphism(() => ({ type: IgnAbsA, factory: () => new IgnAbsA(), properties: [] })),
    abstract: true,
  },
};

describe("polymorphism — ignoreUnrecognizedTags", () => {
  test("a concrete base reads an unknown tag as the base", () => {
    const value = read('{"k":"zzz","V":1}', ignBaseSpec);

    expect(value).toBeInstanceOf(IgnBase);
    expect(value).not.toBeInstanceOf(IgnA);
    expect(value.v).toBe(1);
  });

  test("a known tag still selects its variant", () => {
    expect(read('{"k":"a","V":1}', ignBaseSpec)).toBeInstanceOf(IgnA);
  });

  test("an abstract base still throws NotSupportedException (E7)", () => {
    expectNotSupportedException(
      () => read('{"k":"zzz","V":1}', ignAbsSpec),
      missingDiscriminatorMessage("IgnAbs"),
    );
  });

  test("a non-scalar tag is still rejected", () => {
    expectJsonException(() => read('{"k":null,"V":1}', ignBaseSpec), metadataTokenMessage("Null"));
  });

  test("with out-of-order metadata, a pending $values and an unknown tag is misplaced metadata (V4)", () => {
    const ignAbsAnywhere: TypeSpec<IgnAbs> = {
      ...ignAbsSpec,
      readOptions: { allowOutOfOrderMetadataProperties: true },
    };

    expectJsonException(
      () => read('{"$values":[],"k":"zz"}', ignAbsAnywhere),
      METADATA_NOT_FIRST_MESSAGE,
    );
  });
});

// ─── Misplaced metadata ──────────────────────────────────────────────────────

class CBase {
  constructor(readonly v: number) {}
}

class CDer extends CBase {
  constructor(
    v: number,
    readonly w: number,
  ) {
    super(v);
  }
}

const cBaseSpec: TypeSpec<CBase> = {
  type: CBase,
  factory: (p) => new CBase(p.v as number),
  properties: [{ ts: "v", json: "V", type: primitive }],
  polymorphism: { discriminator: "$type", derived: [{ tag: "d", spec: () => cDerSpec }] },
};

const cDerSpec: TypeSpec<CDer> = {
  type: CDer,
  base: cBaseSpec,
  factory: (p) => new CDer(p.v as number, p.w as number),
  properties: [{ ts: "w", json: "W", type: primitive }],
};

describe("polymorphism — a concrete base rejects a tag that is not first", () => {
  test("a leading tag reads the derived type", () => {
    expect(read('{"$type":"d","V":1,"W":2}', cBaseSpec)).toEqual(new CDer(1, 2));
  });

  const rejected: readonly (readonly [string, string, TypeSpec])[] = [
    ["a registered tag", '{"V":1,"$type":"d","W":2}', cBaseSpec],
    ["an unknown tag", '{"V":1,"$type":"zz"}', cBaseSpec],
    ["an unknown key before the tag", '{"Q":0,"$type":"d","V":1}', cBaseSpec],
    ["a self-registered base tag", '{"V":1,"$type":"b"}', sBaseSpec],
  ];

  for (const [label, json, spec] of rejected) {
    test(`${label} after another key throws JsonException`, () => {
      expectJsonException(() => read(json, spec), METADATA_NOT_FIRST_MESSAGE);
    });
  }

  test("ignoreUnrecognizedTags does not accept a later tag", () => {
    expectJsonException(() => read('{"V":1,"k":"zzz"}', ignBaseSpec), METADATA_NOT_FIRST_MESSAGE);
  });

  test("a custom discriminator after another key throws JsonException", () => {
    expectJsonException(() => read('{"V":1,"k":"a"}', ignBaseSpec), METADATA_NOT_FIRST_MESSAGE);
  });

  test("with out-of-order metadata, a later tag reads the derived type", () => {
    const anywhere: TypeSpec<CBase> = {
      ...cBaseSpec,
      readOptions: { allowOutOfOrderMetadataProperties: true },
    };

    const value = read('{"V":1,"$type":"d","W":2}', anywhere);

    expect(value).toEqual(new CDer(1, 2));
    expect(value).toBeInstanceOf(CDer);
  });

  test("a missing tag still reads the concrete base", () => {
    const value = read('{"V":1}', cBaseSpec);

    expect(value).toBeInstanceOf(CBase);
    expect(value).not.toBeInstanceOf(CDer);
  });
});

describe("polymorphism — array-index keys", () => {
  test("an index key after the tag leaves the tag first (concrete base)", () => {
    expect(read('{"$type":"d","0":5,"V":1,"W":2}', cBaseSpec)).toEqual(new CDer(1, 2));
  });

  test("an index key after the tag leaves the tag first (abstract base)", () => {
    const clip = read('{"kind":"image","0":5,"Path":"a","Seconds":1}', studio.clip);

    expect(clip).toEqual(new ImageClip("a", 1));
  });

  test("an index-like key that is not an array index still counts", () => {
    expectJsonException(
      () => read('{"01":5,"$type":"d","V":1}', cBaseSpec),
      METADATA_NOT_FIRST_MESSAGE,
    );
  });
});

describe("polymorphism — '$'-prefixed keys", () => {
  const dollarRejected: readonly (readonly [string, string, TypeSpec])[] = [
    ["first on a concrete base", '{"$x":1,"V":1}', cBaseSpec],
    ["after the tag", '{"$type":"d","$x":1,"V":1}', cBaseSpec],
    ["later on a concrete base", '{"V":1,"$x":2}', cBaseSpec],
    [
      "after the tag of an abstract base",
      '{"kind":"image","$x":1,"Path":"a","Seconds":1}',
      studio.clip,
    ],
    ["after the self-registered tag", '{"$type":"b","$x":1,"V":1}', sBaseSpec],
    ["$Type, which is not the $type discriminator", '{"$Type":"d","V":1}', cBaseSpec],
    ["$type, under a custom discriminator", '{"V":1,"$type":"a"}', ignBaseSpec],
  ];

  for (const [label, json, spec] of dollarRejected) {
    test(`a $ key ${label} throws JsonException`, () => {
      expectJsonException(() => read(json, spec), DOLLAR_PROPERTY_MESSAGE);
    });
  }

  test("$id and $ref are unsupported metadata", () => {
    expectJsonException(() => read('{"$id":"1","V":1}', cBaseSpec), METADATA_NOT_FIRST_MESSAGE);
    expectJsonException(() => read('{"$ref":"1"}', cBaseSpec), METADATA_NOT_FIRST_MESSAGE);
  });

  test("$values as the first key must follow other metadata", () => {
    expectJsonException(() => read('{"$values":[],"V":1}', cBaseSpec), LEADING_VALUES_MESSAGE);
  });

  const laterValues: readonly string[] = [
    '{"$type":"d","$values":[]}',
    '{"$type":"d","V":1,"$values":[]}',
    '{"V":1,"$values":[]}',
  ];

  for (const json of laterValues) {
    test(`$values in a later position is misplaced metadata: ${json}`, () => {
      expectJsonException(() => read(json, cBaseSpec), METADATA_NOT_FIRST_MESSAGE);
    });
  }

  test("an abstract base reports leading $id / $values before the missing tag", () => {
    expectJsonException(
      () => read('{"$id":"1","Seconds":1}', studio.clip),
      METADATA_NOT_FIRST_MESSAGE,
    );
    expectJsonException(() => read('{"$values":[]}', studio.clip), LEADING_VALUES_MESSAGE);
  });

  test("a leading tag is resolved before later keys are checked", () => {
    expectJsonException(
      () => read('{"$type":"zz","$x":1}', cBaseSpec),
      unrecognizedDiscriminatorMessage("zz"),
    );

    expectJsonException(
      () => read('{"$type":null,"$x":1}', cBaseSpec),
      metadataTokenMessage("Null"),
    );
  });

  test("an abstract base without a leading tag reports the missing tag first", () => {
    expectNotSupportedException(
      () => read('{"$x":1,"kind":"image","Path":"a","Seconds":1}', studio.clip),
      missingDiscriminatorMessage("Clip"),
    );
  });

  test("with out-of-order metadata, a $ key throws before the tag is read", () => {
    const clipAnywhere: TypeSpec<Clip> = {
      ...studio.clip,
      readOptions: { allowOutOfOrderMetadataProperties: true },
    };

    expectJsonException(
      () => read('{"Seconds":1,"$x":2,"kind":"image","Path":"a"}', clipAnywhere),
      DOLLAR_PROPERTY_MESSAGE,
    );
  });

  describe("with out-of-order metadata", () => {
    const cBaseAnywhere: TypeSpec<CBase> = {
      ...cBaseSpec,
      readOptions: { allowOutOfOrderMetadataProperties: true },
    };

    test("errors follow document order: a null tag before a $ key", () => {
      expectJsonException(
        () => read('{"V":1,"$type":null,"$x":1}', cBaseAnywhere),
        metadataTokenMessage("Null"),
      );
    });

    test("an unknown tag is resolved after the walk, so a later $ key wins", () => {
      expectJsonException(
        () => read('{"$type":"zz","$x":1}', cBaseAnywhere),
        DOLLAR_PROPERTY_MESSAGE,
      );
    });

    test("$id after the tag is misplaced metadata", () => {
      expectJsonException(
        () => read('{"V":1,"$type":"d","$id":"1"}', cBaseAnywhere),
        METADATA_NOT_FIRST_MESSAGE,
      );
    });

    const clipAnywhere: TypeSpec<Clip> = {
      ...studio.clip,
      readOptions: { allowOutOfOrderMetadataProperties: true },
    };

    // STJ rows (.NET 10, out-of-order metadata allowed), the 2nd reason naming the contract.
    const twoReasonRows: readonly (readonly [string, string, string, TypeSpec, string])[] = [
      ["F6", '{"V":1,"$values":[]}', "$values", cBaseAnywhere, "CBase"],
      ["T1", '{"$type":"d","V":1,"$values":[]}', "$values", cBaseAnywhere, "CBase"],
      ["P", '{"$values":[],"V":1}', "V", cBaseAnywhere, "CBase"],
      ["S3", '{"$values":[],"$x":1}', "$x", cBaseAnywhere, "CBase"],
      ["T2", '{"$type":"d","$values":[],"V":1}', "V", cBaseAnywhere, "CBase"],
      ["S4", '{"$values":[],"Seconds":1}', "Seconds", clipAnywhere, "Clip"],
    ];

    for (const [row, json, property, spec, typeName] of twoReasonRows) {
      test(`$values beside data key '${property}' gets the two-reason message (${row})`, () => {
        expectJsonException(() => read(json, spec), outOfOrderValuesMessage(property, typeName));
      });
    }

    test("a pending $values without a tag is a leading $values, even on an abstract base (P2, S1)", () => {
      expectJsonException(() => read('{"$values":[]}', clipAnywhere), LEADING_VALUES_MESSAGE);
      expectJsonException(() => read('{"$values":[]}', cBaseAnywhere), LEADING_VALUES_MESSAGE);
    });

    test("a pending $values with a known tag is misplaced metadata (P5, S2)", () => {
      expectJsonException(
        () => read('{"$type":"d","$values":[]}', cBaseAnywhere),
        METADATA_NOT_FIRST_MESSAGE,
      );

      expectJsonException(
        () => read('{"$values":[],"$type":"d"}', cBaseAnywhere),
        METADATA_NOT_FIRST_MESSAGE,
      );
    });

    test("a pending $values with an unknown tag reports the tag first (P9)", () => {
      expectJsonException(
        () => read('{"$values":[],"$type":"zz"}', cBaseAnywhere),
        unrecognizedDiscriminatorMessage("zz"),
      );
    });

    test("after $values, $id is misplaced metadata and the tag's kind is checked (S5, S6)", () => {
      expectJsonException(
        () => read('{"$values":[],"$id":"1"}', cBaseAnywhere),
        METADATA_NOT_FIRST_MESSAGE,
      );

      expectJsonException(
        () => read('{"$values":[],"$type":null}', cBaseAnywhere),
        metadataTokenMessage("Null"),
      );
    });
  });

  test("a non-polymorphic spec reads past a $ key", () => {
    expect(read('{"$x":1,"Path":"a","Seconds":1}', studio.imageClip)).toEqual(
      new ImageClip("a", 1),
    );
  });
});

// ─── Nested polymorphism and intermediate static types ───────────────────────

abstract class NBase {
  constructor(readonly a: number) {}
}

class NMid extends NBase {
  constructor(
    a: number,
    readonly b: number,
  ) {
    super(a);
  }
}

class NLeaf extends NMid {
  constructor(
    a: number,
    b: number,
    readonly c: number,
  ) {
    super(a, b);
  }
}

const nBaseSpec: TypeSpec<NBase> = {
  type: NBase,
  factory: abstractFactory,
  properties: [{ ts: "a", json: "A", type: primitive }],
  polymorphism: {
    discriminator: "t",
    abstract: true,
    derived: [{ tag: "mid", spec: () => nMidSpec }],
  },
};

const nMidSpec: TypeSpec<NMid> = {
  type: NMid,
  base: nBaseSpec,
  factory: (p) => new NMid(p.a as number, p.b as number),
  properties: [{ ts: "b", json: "B", type: primitive }],
  polymorphism: { discriminator: "u", derived: [{ tag: "leaf", spec: () => nLeafSpec }] },
};

const nLeafSpec: TypeSpec<NLeaf> = {
  type: NLeaf,
  base: nMidSpec,
  factory: (p) => new NLeaf(p.a as number, p.b as number, p.c as number),
  properties: [{ ts: "c", json: "C", type: primitive }],
};

describe("polymorphism — nested bases (no re-dispatch, FR-006)", () => {
  test("a middle type through the outer base writes only the outer tag (W17)", () => {
    const json = write(new NMid(1, 2), nBaseSpec);

    expect(json).toBe('{"t":"mid","B":2,"A":1}');
    expect(read(json, nBaseSpec)).toEqual(new NMid(1, 2));
  });

  test("the middle base dispatches its own variants (W18)", () => {
    const json = write(new NLeaf(1, 2, 3), nMidSpec);

    expect(json).toBe('{"u":"leaf","C":3,"B":2,"A":1}');
    expect(read(json, nMidSpec)).toBeInstanceOf(NLeaf);
  });

  test("a leaf registered only on the middle base fails through the outer base (E9)", () => {
    expectNotSupportedException(
      () => write(new NLeaf(1, 2, 3), nBaseSpec),
      unsupportedRuntimeTypeMessage("NLeaf", "NBase"),
    );
  });

  test("the inner tag is ignored when the outer base dispatches", () => {
    const value = read('{"t":"mid","u":"leaf","B":2,"A":1,"C":3}', nBaseSpec);

    expect(value).toBeInstanceOf(NMid);
    expect(value).not.toBeInstanceOf(NLeaf);
  });
});

class HBase {
  constructor(readonly a: number) {}
}

class HMid extends HBase {
  constructor(
    a: number,
    readonly b: number,
  ) {
    super(a);
  }
}

class HLeaf extends HMid {
  constructor(
    a: number,
    b: number,
    readonly c: number,
  ) {
    super(a, b);
  }
}

const hBaseSpec: TypeSpec<HBase> = {
  type: HBase,
  factory: (p) => new HBase(p.a as number),
  properties: [{ ts: "a", json: "A", type: primitive }],
  polymorphism: { discriminator: "t", derived: [{ tag: "leaf", spec: () => hLeafSpec }] },
};

const hMidSpec: TypeSpec<HMid> = {
  type: HMid,
  base: hBaseSpec,
  factory: (p) => new HMid(p.a as number, p.b as number),
  properties: [{ ts: "b", json: "B", type: primitive }],
};

const hLeafSpec: TypeSpec<HLeaf> = {
  type: HLeaf,
  base: hMidSpec,
  factory: (p) => new HLeaf(p.a as number, p.b as number, p.c as number),
  properties: [{ ts: "c", json: "C", type: primitive }],
};

describe("polymorphism — intermediate static type", () => {
  test("the base writes the leaf with every level, derived first (W7)", () => {
    expect(write(new HLeaf(1, 2, 3), hBaseSpec)).toBe('{"t":"leaf","C":3,"B":2,"A":1}');
  });

  test("a non-polymorphic intermediate spec writes no tag and no leaf property (W8)", () => {
    expect(write(new HLeaf(1, 2, 3), hMidSpec)).toBe('{"B":2,"A":1}');
  });

  test("a non-polymorphic intermediate spec ignores the tag on read", () => {
    const value = read('{"t":"leaf","B":2,"A":1,"C":3}', hMidSpec);

    expect(value).toBeInstanceOf(HMid);
    expect(value).not.toBeInstanceOf(HLeaf);
  });
});

// ─── Interface base (no constructor) ─────────────────────────────────────────

interface Shape {
  readonly area: number;
}

class Sq implements Shape {
  constructor(readonly side: number) {}

  get area(): number {
    return this.side * this.side;
  }
}

class Other implements Shape {
  constructor(readonly area: number) {}
}

const sqSpec: TypeSpec<Sq> = {
  type: Sq,
  factory: (p) => new Sq(p.side as number),
  properties: [{ ts: "side", json: "Side", type: primitive }],
};

function interfaceBase(polymorphism: Partial<PolymorphismSpec>): TypeSpec<Shape> {
  return {
    factory: () => {
      throw new Error(
        "Cannot deserialize interface type 'IShape' directly; deserialize it through a polymorphic spec.",
      );
    },
    properties: [{ ts: "area", json: "Area", type: primitive }],
    polymorphism: {
      discriminator: "$type",
      abstract: true,
      typeName: "IShape",
      derived: [{ tag: "sq", spec: () => sqSpec }],
      ...polymorphism,
    },
  };
}

const shapeSpec = interfaceBase({});

describe("polymorphism — interface base (FR-018)", () => {
  test("a class variant dispatches by constructor (W24)", () => {
    const json = write(new Sq(2), shapeSpec);

    expect(json).toBe('{"$type":"sq","Side":2}');
    expect(read(json, shapeSpec)).toBeInstanceOf(Sq);
  });

  test("a missing tag names the interface (E13)", () => {
    expectNotSupportedException(
      () => read('{"Side":2}', shapeSpec),
      missingDiscriminatorMessage("IShape"),
    );
  });

  test("an unregistered implementation fails by default (E14)", () => {
    expectNotSupportedException(
      () => write(new Other(3), shapeSpec),
      unsupportedRuntimeTypeMessage("Other", "IShape"),
    );
  });

  test("fallBackToBaseType writes only the interface members (W25)", () => {
    expect(write(new Other(3), interfaceBase({ unknownDerivedType: "fallBackToBaseType" }))).toBe(
      '{"Area":3}',
    );
  });

  test("fallBackToNearestAncestor with no registered ancestor writes the interface members", () => {
    const nearest = interfaceBase({ unknownDerivedType: "fallBackToNearestAncestor" });

    expect(write(new Other(3), nearest)).toBe('{"Area":3}');
  });

  test("a non-object payload names the interface", () => {
    expectJsonException(() => read("1", shapeSpec), cannotConvertMessage("IShape"));
  });
});
