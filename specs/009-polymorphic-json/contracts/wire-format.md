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
| W14 | web context `clip` (OPEN-4) | `ImageClip("a", 1.5)` | `{"kind":"image","path":"a","seconds":1.5}` | roundtrip | both |
| W15 | `episodeSummaryArray` | `[EpisodeSummary("a","b")]` | `[{"Id":"a","Title":"b"}]` | roundtrip | both |
| W16 | `title` property with `[JsonFactory]` | `"  ok "` | reads as `"ok"` | read | ts (FR-026) |
| W17 | `nBase` (nested) | `NMid(1,2)` | `{"t":"mid","B":2,"A":1}` | roundtrip | both |
| W18 | `nMid` | `NLeaf(1,2,3)` | `{"u":"leaf","C":3,"B":2,"A":1}` (expected; confirmed in S8) | roundtrip | both |
| W19 | `oBase` (overridden property) | `ODer(…)` | `{"$type":"o","X":2,"Name":"x","Base":1}` | roundtrip | both |
| W20 | `gBaseInt32` (closed generic base, FR-015) | `GDer(1, 2)` | `{"$type":"g","Extra":2,"Value":1}` | roundtrip | both |
| W21 | `feed` (closed generic via property, FR-033) | `Feed(Page([EpisodeSummary("a","b")]))` | measured in S8 | roundtrip | both |
| W22 | `listClip` | JSON `null` | `null` | roundtrip | both (OPEN-6) |
| W23 | `iEnumerableClip` | a `Set` / generator of clips (TS) | `[{"kind":"silence","Seconds":2}]` | write | both (FR-038) |
| V1… | one row per value-root kind: `string`, `int32`, `double`, `nullableInt32`, `guid`, `decimal`, `dateTime`, `dateOnly`, `timeOnly`, `dateTimeOffset`, `timeSpan`, numeric enum, `hashSetString`, `dictionaryStringEpisodeSummary`, `dictionaryInt32String` | fixed values | measured in S8 | roundtrip | both, or `divergent` with `stjJson` (spec.md Documented divergences) |

Read-only cases (input → value), direction `read`, both sides:
`{"Path":"a","kind":"image","Seconds":1}` through the `AllowOutOfOrderMetadataProperties` context →
`ImageClip`; `{"k":"zzz","V":1}` through `ignBase` → `IgnBase(1)`;
`{"kind":"image","Path":"a","Seconds":1}` through `imageClip` → `ImageClip` (tag ignored);
`{"t":"leaf","B":2,"A":1,"C":3}` through `hMid` → `HMid(1,2)`;
`{"t":"mid","u":"leaf","B":2,"A":1,"C":3}` through `nBase` → `NMid(1,2)` (inner tag ignored).

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
| E10 | `int32` | JSON `null` | read | `JsonException` | measured in S8 (OPEN-6) |
| E11 | `clip` | JSON `null` | read | `JsonException` (TS) / returns `null` (STJ) | ts row is an error row; stj row is a read row returning `null` (OPEN-6 divergence) |
| E12 | `groupClip` | a `Group` that contains itself | write | `JsonException` | `A possible object cycle was detected.` (FR-016) |

Type names inside TS messages are the runtime class names (`constructor.name`), which equal the C#
simple names unless a `[Name]` override renames the class; fixtures use no overrides. An
unregistered runtime type has no C# metadata on the TS side, so carrying C# names in the descriptor
would not make E8 exact anyway.

The TS message is compared as a prefix because STJ appends `Path: … | LineNumber: … |
BytePositionInLine: …` (OPEN-5).

## Not covered

See spec.md "Documented divergences". In particular, no fixture uses duplicate keys, `1.0` int
tags, `1E+21` / `-0`, or integer-like property names in a polymorphic hierarchy.
