# Contract: JSON wire format for unions

**Normative for**: FR-023–FR-026; D5. The reference is System.Text.Json `11.0.0-rc.1.26425.128`. Every row below was measured with a .NET 11 probe, by reflection and with source-gen (research R5). A row marked *(fixture)* must exist as a checked fixture in `targets/js/sample-unions/test/fixtures/`.

## Model

```csharp
public record ImageClip(string Path, double Seconds);
public record SilenceClip(double Seconds);
public record Img(string Path);
public record Sil(double Seconds);

public union Clip(ImageClip, SilenceClip);   // ambiguous: both Object → MS0035
public union Id(int, string);                // unambiguous
public union Mixed(int, string, Img);        // unambiguous
public union NId(int?, string);              // nullable case

public record Holder(Clip Clip, Id Id);
public record Bag(Dictionary<string, Id> Map, Id[] Arr, Id? Opt);

[JsonSerializable(typeof(Holder))]
[JsonSerializable(typeof(Bag))]
public partial class UnionJsonContext : JsonSerializerContext;
```

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

TS rule: `null` is written as `null`. Otherwise the converter picks the **first** case whose runtime test matches and writes that case's descriptor output. Property names follow the context's naming policy, exactly as for non-union properties.

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

## Build-time diagnostic (FR-026)

`MS0035 AmbiguousUnionJsonCases` (Warning), once per JSON-reachable union:

```text
warning MS0035: Union type 'Clip': case types 'ImageClip', 'SilenceClip' all serialize as JSON value type 'Object'. System.Text.Json reports the same ambiguity (SYSLIB1227) and cannot deserialize this union without a custom type classifier, which Metano does not transpile.
```

## Out of scope (v1)

- `[JsonSerializable(typeof(Clip))]` as a **root** (OD-8).
- `[JsonUnion(TypeClassifier = …)]`, `JsonUnionTypeStructuralClassifier` and `JsonSerializerOptions.TypeClassifiers` → MS0031.
- `[JsonPolymorphic]` bases as cases (OD-7, #268) → MS0031.
- Doubles in exponent form, `-0`, `NaN`/`Infinity` (STJ throws by default): no fixtures.
- Case records that inherit properties, because STJ writes derived properties first (research R5).
