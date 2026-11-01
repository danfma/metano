# Contract: JSON wire format for unions

**Normative for**: FR-023–FR-026, FR-030, FR-037, FR-038, and the JSON side of FR-039 / FR-040; D5, D10, D11. The reference is System.Text.Json `11.0.0-rc.1.26425.128`. Every row below was measured with a .NET 11 probe, by reflection and with source-gen (research R5). A row marked *(fixture)* must exist as a checked fixture in `targets/js/sample-unions/test/fixtures/`.

## Model

```csharp
public record ImageClip(string Path, double Seconds);
public record SilenceClip(double Seconds);
public record Img(string Path);
public record Sil(double Seconds);

public union Clip(ImageClip, SilenceClip);   // ambiguous: both Object → MS0042
public union Id(int, string);                // unambiguous
public union Mixed(int, string, Img);        // unambiguous
public union NId(int?, string);              // nullable case

public record Holder(Clip Clip, Id Id);
public record Bag(Dictionary<string, Id> Map, Id[] Arr, Id? Opt);

[JsonSerializable(typeof(Holder))]
[JsonSerializable(typeof(Bag))]
public partial class UnionJsonContext : JsonSerializerContext;
```

### Contract closure (FR-030)

`Holder` and `Bag` are the only roots, and no root names a case record. The generated context still MUST declare a spec getter for every object-kind case it reaches through a union (`imageClip`, `silenceClip`, and `img` when `Mixed` is reachable), and MUST NOT declare a spec for any union (`clip`, `id`). Each union is described inline:

```ts
// shape only; the exact literal is fixed by the 010d golden
{ kind: "union", name: "Clip", cases: [
  { token: "object", test: (v) => v instanceof ImageClip,   descriptor: () => ({ kind: "ref", spec: () => this.imageClip }) },
  { token: "object", test: (v) => v instanceof SilenceClip, descriptor: () => ({ kind: "ref", spec: () => this.silenceClip }) },
] }
```

A constructed generic union (`Opt<int>`) gets its own inline descriptor with the substituted cases, and its `name` is STJ's display name for that instance. Research R10 measured ``GOpt`1[Id]``; other forms (several or nested type arguments) are pinned by fixtures.

## Serialization (bare payload, no discriminator)

| C# value | JSON |
|---|---|
| `Id` = `5` | `5` *(fixture)* |
| `Id` = `"x"` | `"x"` *(fixture)* |
| `default(Id)` | `null` *(fixture)* |
| `Clip` = `new ImageClip("a.png", 1.5)` | `{"Path":"a.png","Seconds":1.5}` *(fixture)* |
| `Clip` = `new SilenceClip(2)` | `{"Seconds":2}` *(fixture)* |
| `new Holder(new SilenceClip(3), "abc")` | `{"Clip":{"Seconds":3},"Id":"abc"}` *(fixture)* |
| `List<Clip>` of an image and a silence | `[{"Path":"p","Seconds":1},{"Seconds":2}]` *(fixture)* |
| `new Bag({"k":5,"s":"x"}, [1,"a"], null)` | `{"Map":{"k":5,"s":"x"},"Arr":[1,"a"],"Opt":null}` *(fixture)* |

TS rule: `null` is written as `null`, which also covers a widened slot holding the erased `default(U)` (FR-039). Otherwise the converter picks the **first** case, in declaration order, whose runtime test matches and writes that case's descriptor output. For a union with overlapping cases (the MS0039 warning) that is the earlier case (FR-040); STJ writes the payload of the actual C# case, so the bytes can differ only where the two cases' descriptors write the same value differently. Property names follow the context's naming policy, exactly as for non-union properties.

## Deserialization (token kind only)

The candidates are the cases whose token kind equals the input's token kind (see the table in [data-model.md](../data-model.md)).

| Union | Input | Result |
|---|---|---|
| `Id` | `5` | `5` (number) *(fixture)* |
| `Id` | `"x"` | `"x"` *(fixture)* |
| `Id` | `null` | `null` *(fixture)* |
| `Mixed` | `{"Path":"a"}` | `Img { Path = "a" }` *(fixture)* |
| `Id` | `true` | throws `JSON token type 'True' is not supported for union type 'Id'.` *(fixture)* |
| `Id` | `{}` | throws `JSON token type 'StartObject' is not supported for union type 'Id'.` *(fixture)* |
| `Id` | `5.5` | throws `The JSON value could not be converted to System.Int32.` *(fixture)* |
| `Clip` | any object | throws `JSON value type 'Object' is ambiguous for union type 'Clip' because multiple case types can use this value type. Specify a custom type classifier to support deserialization.` *(fixture)* |
| `Clip` | `5` | throws `JSON token type 'Number' is not supported for union type 'Clip'.` *(fixture)* |

- **Token names** in the "not supported" message are STJ's `JsonTokenType` names: `Number`, `String`, `True`, `False`, `StartObject`, `StartArray`.
- **Kind names** in the "ambiguous" message are STJ's `JsonValueKind`-style names: `Object`, `Array`, `Number`, `String`.
- **The `5.5` → `Int32` message** comes from the delegated case converter. The TS `primitive` descriptor for `int` must reject a non-integer number with STJ's message. Doing that for every `int` descriptor would change their behavior, so in v1 only union cases reject non-integers. Whether all `int` descriptors should follow is decided separately (plan.md risk K7).
- **Paths**: STJ appends ` Path: $.Clip | LineNumber: … | BytePositionInLine: …`. The TS runtime reproduces the **message prefix** exactly, appends ` Path: <json-path>`, and does not emulate line or byte positions. Fixtures compare the prefix up to `Path:`.

## Options and token parity (FR-037)

| Context options / case | STJ behaviour (research R10) | Metano |
|---|---|---|
| `JsonSerializerDefaults.Web` or `NumberHandling = AllowReadingFromString`, `Id(int, string)`, input `"5"` | throws `JSON value type 'String' is ambiguous for union type 'Id' …` | same message; MS0042 reports the `String` overlap at build time *(fixture)* |
| same options, input `5` | `Int32 5` | `5` *(fixture)* |
| `NumberHandling = WriteAsString` or `AllowNamedFloatingPointLiterals` with a number case | number written as a string | not reproduced: MS0038 |
| `byte[]` case | writes `"AQI="` (base64 string token) | not reproduced (descriptor writes an array): MS0038 (OD-16) |
| plain enum case | writes `1` (number token) | `number` token |
| `[StringEnum]` case without `JsonStringEnumConverter` | writes a number | not reproduced (Metano writes a string): MS0038 (OD-16) |
| enum case with `JsonStringEnumConverter` | writes a string | `string` token |
| `char` case | writes a string | not reproduced (no `char` descriptor): MS0038 (OD-16) |
| `Dictionary<Id, V>` | throws `NotSupportedException` (not a supported dictionary key) | MS0038 |
| `DeserializeAsync<Id>` | identical to `Deserialize` | single TS entry point; nothing extra |

## Cross-package (FR-038)

The library declares `Id`, `Clip`, `ImageClip` and `SilenceClip`; the consumer declares `Holder(Clip Clip, Id Id)` and the context.

- `Id` gets its inline descriptor in the consumer; its case tests come from the classification, with any guard imported from the library package.
- `Clip` gets MS0038 on the `Holder.Clip` property: `ImageClip` and `SilenceClip` are object-kind cases from another assembly, and the consumer context declares no spec for metadata types (OD-13). The consumer context never contains a `this.imageClip` thunk.

## Build-time diagnostic (FR-026)

`MS0042 AmbiguousUnionJsonCases` (Warning), once per JSON-reachable union:

```text
warning MS0042: Union type 'Clip': case types 'ImageClip', 'SilenceClip' all serialize as JSON value type 'Object'. System.Text.Json reports the same ambiguity (SYSLIB1227) and cannot deserialize this union without a custom type classifier, which Metano does not transpile.
```

## Out of scope (v1)

- `[JsonSerializable(typeof(Clip))]` as a **root** → MS0038 until the #270 `ValueSpec` roots of [009-polymorphic-json](../../009-polymorphic-json/spec.md) land (OD-8).
- `[JsonUnion(TypeClassifier = …)]`, `JsonUnionTypeStructuralClassifier` and `JsonSerializerOptions.TypeClassifiers` → MS0038.
- `[JsonPolymorphic]` bases as cases → MS0038 until #268 lands through [009-polymorphic-json](../../009-polymorphic-json/spec.md) (OD-7).
- Doubles in exponent form, `-0`, `NaN`/`Infinity` (STJ throws by default): no fixtures.
- Case records that inherit properties, because STJ writes derived properties first (research R5).
- Union dictionary keys (STJ does not support them either) → MS0038.
- Cases STJ writes differently from Metano's descriptors (`byte[]`, `char`, `[StringEnum]` without a converter) → MS0038 (OD-16).
- Object-kind cases from another assembly in a consumer context → MS0038 (OD-13).
