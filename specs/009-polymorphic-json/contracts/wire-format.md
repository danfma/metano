# Contract: JSON wire format (STJ parity)

## Shared manifest

All cases live in **one** checked-in manifest,
`targets/js/sample-json-contracts/test/fixtures/stj/cases.json`, read by both the .NET test
(`StjConformanceFixtureTests`) and the Bun test (`stj-conformance.test.ts`). Neither side defines
case values or expected text on its own.

```ts
interface ConformanceCase {
  id: string;                         // "W1", "E3", …
  context: string;                    // context class, e.g. "StudioJsonContext"
  getter: string;                     // TS getter / STJ property camel-cased, e.g. "clip"
  direction: "roundtrip" | "write" | "read";
  sides?: ("stj" | "ts")[];           // default both
  expectedClass?: string;             // read / roundtrip rows only
  json?: string;                      // exact text (write: produced; read: input)
  stjJson?: string;                   // divergent rows: what STJ writes when it differs from `json`
  error?: { class: "JsonException" | "NotSupportedException"; message: string };
  divergence?: string;                // id of the entry in spec.md "Documented divergences"
}
```

- Each side builds values through a **per-id builder** (`StjCaseBuilders` in C#, `builders.ts` in
  TS). A manifest id with no builder on a side it lists fails that side's test; a builder with no
  manifest id fails too.
- **write**: build → serialize → compare to `json` byte-for-byte (STJ side: `stjJson ?? json`).
- **read**: `json` → deserialize → assert `expectedClass` (and field values via the builder's
  expected value); `instanceof` is asserted only for read and roundtrip rows.
- **roundtrip**: write, then read the written text.
- **error**: the operation of `direction` throws `error.class` and the message starts with
  `error.message`.
- The .NET side checks every row whose `sides` include `stj`; the Bun side every row whose `sides`
  include `ts`.

## Valid cases

| # | Spec used | Value | JSON text | Direction | Sides |
|---|---|---|---|---|---|
| W1 | `clip` | `ImageClip("a.png", 1.5)` | `{"kind":"image","Path":"a.png","Seconds":1.5}` | roundtrip | both |
| W2 | `clip` | `SilenceClip(2)` | `{"kind":"silence","Seconds":2}` | roundtrip | both |
| W3 | `imageClip` | `ImageClip("a.png", 1.5)` | `{"Path":"a.png","Seconds":1.5}` | roundtrip | both |
| W4 | `timeline` | `Timeline("t", ImageClip("a.png", 1.5), [SilenceClip(2)])` | `{"Name":"t","Main":{"kind":"image","Path":"a.png","Seconds":1.5},"Clips":[{"kind":"silence","Seconds":2}]}` | roundtrip | both |
| W5 | `listClip` | `[ImageClip("a",1.5), SilenceClip(2)]` | `[{"kind":"image","Path":"a","Seconds":1.5},{"kind":"silence","Seconds":2}]` | roundtrip | both |
| W6 | `dictionaryStringClip` | `{x: SilenceClip(2)}` | `{"x":{"kind":"silence","Seconds":2}}` | roundtrip | both |
| W7 | `hBase` | `HLeaf(1,2,3)` | `{"t":"leaf","C":3,"B":2,"A":1}` | roundtrip | both |
| W8 | `hMid` | `HLeaf(1,2,3)` | `{"B":2,"A":1}` | write | both |
| W9 | `iBase` | `IA(5)` (int tag `1`) | `{"$type":1,"X":5}` | roundtrip | both |
| W10 | `iBase` | `IS(2)` (string tag `"s"`) | `{"$type":"s","Y":2}` | roundtrip | both |
| W11 | `dBase` (concrete base, not self-registered) | `DBase()` | `{}` | roundtrip | both |
| W11b | `sBase` (concrete base registered as `"b"`) | `SBase(1)` | `{"$type":"b","V":1}` | roundtrip (reads as `SBase`) | both |
| W12 | `uNear` (`FallBackToNearestAncestor`) | `UNearB(1,2,3)` | `{"$type":"a","B":2,"A":1}` | write (reads back as `UNearA`, not `UNearB`) | both |
| W13 | `uBaseH` (`FallBackToBaseType`, abstract base) | `UBaseX(1,2)` | `{"A":1}` | write (reading it back throws, E3-style) | both |
| W14 | web context `clip` (FR-048) | `ImageClip("a", 1.5)` | `{"kind":"image","path":"a","seconds":1.5}` | roundtrip | both |
| W15 | `episodeSummaryArray` | `[EpisodeSummary("a","b")]` | `[{"Id":"a","Title":"b"}]` | roundtrip | both |
| W16 | `title` property with `[JsonFactory]` | `"  ok "` | reads as `"ok"` | read | ts (FR-026) |
| W17 | `nBase` (nested) | `NMid(1,2)` | `{"t":"mid","B":2,"A":1}` | roundtrip | both |
| W18 | `nMid` | `NLeaf(1,2,3)` | `{"u":"leaf","C":3,"B":2,"A":1}` (expected; confirmed in S8) | roundtrip | both |
| W19 | `oBase` (overridden property) | `ODer(…)` | `{"$type":"o","X":2,"Name":"x","Base":1}` | roundtrip | both |
| W20 | `gBaseInt32` (closed generic base, FR-015) | `GDer(1, 2)` | `{"$type":"g","Extra":2,"Value":1}` | roundtrip | both |
| W21 | `feed` (closed generic via property, FR-033) | `Feed(Page([EpisodeSummary("a","b")]))` | measured in S8 | roundtrip | both |
| W22 | `listClip` | JSON `null` | `null` | roundtrip | both (FR-037) |
| W22b | `clip` | JSON `null` | `null` | roundtrip | both (FR-037, breaking: was an error on the TS side) |
| W23 | `iEnumerableClip` | a `Set` / generator of clips (TS) | `[{"kind":"silence","Seconds":2}]` | write | both (FR-038) |
| W24 | `shape` (interface base, FR-018) | `Sq(2)` | `{"$type":"sq","Side":2}` | roundtrip | both |
| W25 | `shapeB` (interface base, `FallBackToBaseType`) | `OtherB(3)` (unregistered) | `{"Area":3}` | write | both |
| W26 | `plainShape` (`[PlainObject]` variants, FR-019) | `Point(1)` / `{ x: 1 }` | `{"$type":"point","X":1}` | roundtrip (TS reads a plain object) | both |
| W27 | `plainShape` | `Point3(1,2,3)` / `{ x: 1, y: 2, z: 3 }` | `{"$type":"point3","Y":2,"Z":3,"X":1}` (expected; confirmed in S8) | roundtrip | both (selects `Point3` by `testOrder`) |
| W28 | `mixedShape` (class and `[PlainObject]` variants on one base) | one value of each family | measured in S8 | roundtrip | both |
| V1… | one row per value-root kind: `string`, `int32`, `double`, `nullableInt32`, `guid`, `decimal`, `dateTime`, `dateOnly`, `timeOnly`, `dateTimeOffset`, `timeSpan`, numeric enum, `hashSetString`, `dictionaryStringEpisodeSummary`, `dictionaryInt32String` | fixed values | measured in S8 | roundtrip | both, or `divergent` with `stjJson` (spec.md Documented divergences) |

Web-context cases (`[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]`, FR-048), both sides
unless noted, all measured in research.md R8:

| # | Spec | Input / value | Expected | Direction |
|---|---|---|---|---|
| WEB1 | `imageClip` | `{"PATH":"a","seconds":1}` | `ImageClip("a", 1)` | read |
| WEB2 | `imageClip` | `{"path":"a","Path":"b","seconds":1}` | `ImageClip("b", 1)` (last key wins) | read |
| WEB3 | `nums` | `{"n":"5","m":"7","d":"1.10","l":["1",2],"map":{"k":"3"}}` | `n = 5`, `m = 7`, `d = 1.10`, `l = [1, 2]`, `map.k = 3` | read |
| WEB4 | `nums` | `{"n":"+5"}`, `{"n":"007"}`, `{"n":"-5"}` | `5`, `7`, `-5` | read |
| WEB5 | `nums` | `{"x":"1e2"}`, `{"x":"+1.5"}`, `{"x":"NaN"}`, `{"x":"Infinity"}`, `{"x":"-Infinity"}` | `100`, `1.5`, `NaN`, `Infinity`, `-Infinity` | read |
| WEB6 | `int32` | `"5"` | `5` | read |
| WEB7 | `nums` | `Nums(n: 5, m: 7, d: 1.10, x: 2.5)` | `{"n":5,"m":7,"d":1.10,"x":2.5,"l":null,"map":null,"c":0}` | write |

Read-only cases (input → value), direction `read`, both sides:
`{"Path":"a","kind":"image","Seconds":1}` through the `AllowOutOfOrderMetadataProperties` context →
`ImageClip`; `{"k":"zzz","V":1}` through `ignBase` → `IgnBase(1)`;
`{"kind":"image","Path":"a","Seconds":1}` through `imageClip` → `ImageClip` (tag ignored);
`{"t":"leaf","B":2,"A":1,"C":3}` through `hMid` → `HMid(1,2)`;
`{"t":"mid","u":"leaf","B":2,"A":1,"C":3}` through `nBase` → `NMid(1,2)` (inner tag ignored);
`{"$type":"point","X":1}` through `plainShape` → plain object `{ x: 1 }` (the tag, not the shape,
selects the variant).

## Error cases

| # | Spec | Input | Direction | Exception | Message prefix |
|---|---|---|---|---|---|
| E1 | `clip` | `{"kind":"video","Seconds":2}` | read | `JsonException` | `Read unrecognized type discriminator id 'video'.` |
| E2 | `clip` | `{"kind":"Image","Seconds":2}` | read | `JsonException` | `Read unrecognized type discriminator id 'Image'.` |
| E3 | `clip` | `{"Seconds":2}` | read | `NotSupportedException` | `The JSON payload for polymorphic interface or abstract type 'Clip' must specify a type discriminator.` |
| E4 | `clip` | `{"Path":"a","kind":"image","Seconds":1}` | read | `NotSupportedException` | same as E3 |
| E5 | `clip` | `{"kind":null,"Seconds":2}` | read | `JsonException` | `The '$id', '$ref' or '$type' metadata properties must be JSON strings. Current token type is 'Null'.` |
| E5b | `clip` | `{"kind":true,…}` / `false` / `{}` / `[]` | read | `JsonException` | same, with `'True'` / `'False'` / `'StartObject'` / `'StartArray'` |
| E6 | `iBase` | `{"$type":"1","X":5}` | read | `JsonException` | `Read unrecognized type discriminator id '1'.` |
| E7 | `ignAbs` | `{"k":"zzz","V":1}` | read | `NotSupportedException` | `The JSON payload for polymorphic interface or abstract type 'IgnAbs' must specify a type discriminator.` |
| E8 | `uFail` | `UFailX(1)` | write | `NotSupportedException` | `Runtime type 'UFailX' is not supported by polymorphic type 'UFail'.` |
| E9 | `nBase` | `NLeaf(1,2,3)` (registered only on `NMid`) | write | `NotSupportedException` | `Runtime type 'NLeaf' is not supported by polymorphic type 'NBase'.` |
| E10 | `int32` | JSON `null` | read | `JsonException` | measured in S8 (FR-037) |
| E11 | `sPoint` (`record struct`) | JSON `null` | read | `JsonException` | `The JSON value could not be converted to SPoint.` (FR-037) |
| E12 | `groupClip` | a `Group` that contains itself | write | `JsonException` | `A possible object cycle was detected.` (FR-016) |
| E13 | `shape` | `{"Side":2}` | read | `NotSupportedException` | `The JSON payload for polymorphic interface or abstract type 'IShape' must specify a type discriminator.` |
| E14 | `shape` | `Other()` (unregistered implementation) | write | `NotSupportedException` | `Runtime type 'Other' is not supported by polymorphic type 'IShape'.` |
| E15 | `plainShape` | a plain object matching no guard (TS only) | write | `NotSupportedException` | `Runtime type 'Object' is not supported by polymorphic type 'Shape'.` (ts only: STJ has no shape-less value) |
| E16 | web `clip` | `{"Kind":"image","path":"a","seconds":1}` | read | `NotSupportedException` | `The JSON payload for polymorphic interface or abstract type 'Clip' must specify a type discriminator.` |
| E17 | web `nums` | `{"n":"5.0"}`, `{"n":" 5"}`, `{"n":"5 "}`, `{"n":""}`, `{"n":"1e2"}`, `{"n":"0x10"}` | read | `JsonException` | `The JSON value could not be converted to System.Int32.` |
| E18 | web `nums` | `{"x":"nan"}`, `{"x":" 1.5"}`, `{"x":"0x1"}` | read | `JsonException` | `The JSON value could not be converted to System.Double.` (measured: `JsonException`; text pinned in S8) |
| E19 | web `nums` | `{"c":"1"}` (enum) / `{"m":"null"}` (`int?`) | read | `JsonException` | `The JSON value could not be converted to Color.` / `… System.Nullable`1[System.Int32].` |
| E20 | web `iBase` | `{"$type":"1","x":5}` | read | `JsonException` | `Read unrecognized type discriminator id '1'.` |

Type names inside TS messages are the runtime class names (`constructor.name`), which equal the C#
simple names unless a `[Name]` override renames the class; fixtures use no overrides. An
unregistered runtime type has no C# metadata on the TS side, so carrying C# names in the descriptor
would not make E8 exact anyway.

The TS message is compared as a prefix because STJ appends `Path: … | LineNumber: … |
BytePositionInLine: …` (DECIDED-5).

## Not covered

See spec.md "Documented divergences". In particular, no fixture uses duplicate keys, `1.0` int
tags, `1E+21` / `-0`, or integer-like property names in a polymorphic hierarchy.
