# Contract: JSON wire format (STJ parity)

The generated TypeScript serializer MUST produce exactly the text below (via `JSON.stringify` of
`serialize(...)`) and MUST read it back to the stated value. Every row becomes a conformance fixture
that the .NET side proves against real STJ source-gen output (plan.md "Test strategy").

## Valid cases

| # | Spec used | Value | JSON text |
|---|---|---|---|
| W1 | `clip` | `ImageClip("a.png", 1.5)` | `{"kind":"image","Path":"a.png","Seconds":1.5}` |
| W2 | `clip` | `SilenceClip(2)` | `{"kind":"silence","Seconds":2}` |
| W3 | `imageClip` | `ImageClip("a.png", 1.5)` | `{"Path":"a.png","Seconds":1.5}` |
| W4 | `timeline` | `Timeline("t", ImageClip("a.png", 1.5), [SilenceClip(2)])` | `{"Name":"t","Main":{"kind":"image","Path":"a.png","Seconds":1.5},"Clips":[{"kind":"silence","Seconds":2}]}` |
| W5 | `listClip` | `[ImageClip("a",1.5), SilenceClip(2)]` | `[{"kind":"image","Path":"a","Seconds":1.5},{"kind":"silence","Seconds":2}]` |
| W6 | `dictionaryStringClip` | `{x: SilenceClip(2)}` | `{"x":{"kind":"silence","Seconds":2}}` |
| W7 | `hBase` | `HLeaf(1,2,3)` | `{"t":"leaf","C":3,"B":2,"A":1}` |
| W8 | `hMid` | `HLeaf(1,2,3)` | `{"B":2,"A":1}` |
| W9 | `iBase` | `IA(5)` (int tag `1`) | `{"$type":1,"X":5}` |
| W10 | `iBase` | `IS(2)` (string tag `"s"`) | `{"$type":"s","Y":2}` |
| W11 | `dBase` (concrete base) | `DBase()` | `{}` |
| W12 | `uNear` (`FallBackToNearestAncestor`) | `UNearB(1,2,3)` | `{"$type":"a","B":2,"A":1}` |
| W13 | `uBaseH` (`FallBackToBaseType`) | `UBaseX(1,2)` | `{"A":1}` |
| W14 | web context `clip` (OPEN-4) | `ImageClip("a", 1.5)` | `{"kind":"image","path":"a","seconds":1.5}` |
| W15 | `episodeSummaryArray` | `[EpisodeSummary("a","b")]` | `[{"Id":"a","Title":"b"}]` |
| W16 | `title` property with `[JsonFactory]` | `"  ok "` (read) | reads as `"ok"` |

Read-only cases (input → value): `{"Path":"a","kind":"image","Seconds":1}` through the
`AllowOutOfOrderMetadataProperties` context → `ImageClip`; `{"k":"zzz","V":1}` through `ignBase`
→ `IgnBase(1)`; `{"kind":"image","Path":"a","Seconds":1}` through `imageClip` → `ImageClip`
(tag ignored); `{"t":"leaf","B":2,"A":1,"C":3}` through `hMid` → `HMid(1,2)`.

## Error cases

| # | Spec | Input | Exception | Message prefix |
|---|---|---|---|---|
| E1 | `clip` | `{"kind":"video","Seconds":2}` | `JsonException` | `Read unrecognized type discriminator id 'video'.` |
| E2 | `clip` | `{"kind":"Image","Seconds":2}` | `JsonException` | `Read unrecognized type discriminator id 'Image'.` |
| E3 | `clip` | `{"Seconds":2}` | `NotSupportedException` | `The JSON payload for polymorphic interface or abstract type 'Clip' must specify a type discriminator.` |
| E4 | `clip` | `{"Path":"a","kind":"image","Seconds":1}` | `NotSupportedException` | same as E3 |
| E5 | `clip` | `{"kind":null,"Seconds":2}` | `JsonException` | `The '$id', '$ref' or '$type' metadata properties must be JSON strings. Current token type is 'Null'.` |
| E6 | `iBase` | `{"$type":"1","X":5}` | `JsonException` | `Read unrecognized type discriminator id '1'.` |
| E7 | `ignAbs` | `{"k":"zzz","V":1}` | `NotSupportedException` | `The JSON payload for polymorphic interface or abstract type 'IgnAbs' must specify a type discriminator.` |
| E8 | `uFail` (serialize) | `UFailX(1)` | `NotSupportedException` | `Runtime type 'UFailX' is not supported by polymorphic type 'UFail'.` |

Type names inside TS messages are the runtime class names (`constructor.name`), which equal the C#
simple names unless a `[Name]` override renames the class; fixtures use no overrides. An
unregistered runtime type has no C# metadata on the TS side, so carrying C# names in the descriptor
would not make E8 exact anyway.

The TS message is compared as a prefix because STJ appends `Path: … | LineNumber: … |
BytePositionInLine: …` (OPEN-5).

## Not covered (documented divergences)

- Duplicate tag keys (`JSON.parse` keeps the last one).
- `1E+21`, `-0` number text.
- Integer-like property names (rejected at transpile time, `MS0032`).
