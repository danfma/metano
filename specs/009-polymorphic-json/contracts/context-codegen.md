# Contract: generated `SerializerContext` output

The shapes below are normative for golden files. Indentation and line breaks follow the printer;
only the members shown change. Value imports stay limited to `metano-runtime/system/json` plus the
user types (classes and branded/enum companions, as today). Types used only inside a
`TypeSpec<…>` / `ValueSpec<…>` type argument (`HashSet`, `Temporal`, `Decimal`, `UUID`,
cross-package element types) are imported **type-only** by the context transformer (FR-041); the
golden files assert those import lines.

## 1. Polymorphic hierarchy (#268)

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ImageClip), "image")]
[JsonDerivedType(typeof(SilenceClip), "silence")]
public abstract record Clip(double Seconds);

public sealed record ImageClip(string Path, double Seconds) : Clip(Seconds);

public sealed record SilenceClip(double Seconds) : Clip(Seconds);

public sealed record Timeline(string Name, Clip Main, IReadOnlyList<Clip> Clips);

[JsonSerializable(typeof(Timeline))]
public partial class StudioJsonContext : JsonSerializerContext;
```

```ts
import { SerializerContext, type TypeSpec } from "metano-runtime/system/json";
import { Clip } from "./clip";
import { ImageClip } from "./image-clip";
import { SilenceClip } from "./silence-clip";
import { Timeline } from "./timeline";

export class StudioJsonContext extends SerializerContext {
  // _default, default, _timeline, _clip, _imageClip, _silenceClip fields as today

  get timeline(): TypeSpec<Timeline> { /* main: ref → this.clip; clips: array of ref → this.clip */ }

  get clip(): TypeSpec<Clip> {
    return this._clip ??= this.createSpec({
      type: Clip,
      factory: (p: Record<string, unknown>) => {
        throw new Error("Cannot deserialize abstract type 'Clip' directly; deserialize it through a polymorphic spec.");
      },
      properties: [{ ts: "seconds", json: "Seconds", type: { kind: "primitive" } }],
      polymorphism: {
        discriminator: "kind",
        abstract: true,
        derived: [
          { tag: "image", spec: () => this.imageClip },
          { tag: "silence", spec: () => this.silenceClip },
        ],
      },
    });
  }

  // Added by the closure (FR-003) although not listed in [JsonSerializable].
  get imageClip(): TypeSpec<ImageClip> {
    return this._imageClip ??= this.createSpec({
      type: ImageClip,
      base: this.clip,
      factory: (p: Record<string, unknown>) => new ImageClip(p.path as string, p.seconds as number),
      properties: [{ ts: "path", json: "Path", type: { kind: "primitive" } }],
    });
  }

  get silenceClip(): TypeSpec<SilenceClip> { /* base: this.clip, no own properties */ }
}
```

Rules:

- `polymorphism` is the **last** key of the spec literal and appears only on polymorphic types.
- `abstract: true` only when the C# type is abstract. `unknownDerivedType`,
  `ignoreUnrecognizedTags`, `allowOutOfOrderMetadata` only when not the default.
- A string tag prints as a string literal, an int tag as a numeric literal, an untagged entry as
  `{ spec: () => this.x }`.
- Derived entries keep attribute order. Each `spec` is an arrow returning `this.<getter>`.
- The abstract factory message of a **polymorphic** base is reworded (FR-011); a non-polymorphic
  abstract base keeps the #238 text.
- A base that registers itself gets an entry pointing at its own getter:
  `{ tag: "b", spec: () => this.sBase }`.
- A closed generic polymorphic base (FR-015) uses the constructed name everywhere:
  `get gBaseInt32(): TypeSpec<GBase<number>>`, and `GDer`'s spec links `base: this.gBaseInt32`.
- Property lists are deduplicated by final JSON name; an overridden property appears only in the
  most-derived spec (FR-009).

## 2. Non-object roots (#270)

```csharp
[JsonSerializable(typeof(EpisodeSummary))]
[JsonSerializable(typeof(IReadOnlyList<EpisodeSummary>))]
[JsonSerializable(typeof(EpisodeSummary[]))]
[JsonSerializable(typeof(Dictionary<string, EpisodeSummary>))]
[JsonSerializable(typeof(HashSet<string>))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(List<string>), TypeInfoPropertyName = "Names")]
public partial class AppJsonContext : JsonSerializerContext;
```

```ts
import type { HashSet } from "metano-runtime";
import { SerializerContext, type TypeSpec, type ValueSpec } from "metano-runtime/system/json";
import { EpisodeSummary } from "./episode-summary";

export class AppJsonContext extends SerializerContext {
  private _episodeSummary?: TypeSpec<EpisodeSummary>;
  private _iReadOnlyListEpisodeSummary?: ValueSpec<EpisodeSummary[] | null>;
  private _episodeSummaryArray?: ValueSpec<EpisodeSummary[] | null>;
  private _dictionaryStringEpisodeSummary?: ValueSpec<Map<string, EpisodeSummary> | null>;
  private _hashSetString?: ValueSpec<HashSet<string> | null>;
  private _nullableInt32?: ValueSpec<number | null>;
  private _names?: ValueSpec<string[] | null>;

  get iReadOnlyListEpisodeSummary(): ValueSpec<EpisodeSummary[] | null> {
    return this._iReadOnlyListEpisodeSummary ??= {
      descriptor: { kind: "array", element: { kind: "ref", spec: () => this.episodeSummary } },
      acceptsNull: true, // FR-037: reference-type root
    };
  }

  get nullableInt32(): ValueSpec<number | null> {
    return this._nullableInt32 ??= {
      descriptor: { kind: "nullable", inner: { kind: "primitive" } },
    };
  }

  // other getters follow the same pattern
}
```

Rules:

- Object roots keep `createSpec`; value roots use a plain object literal (no registration: value
  specs have no constructor to resolve).
- The field/getter type argument is the mapped TS type of the root (FR-032), plus `| null` for a
  reference-type value root (FR-037). `nullableInt32` is already `number | null` through its
  `nullable` descriptor and has no `acceptsNull`.
- `IEnumerable<T>` / `IReadOnlyCollection<T>` roots are typed `ValueSpec<Iterable<T> | null>` and
  serialize any iterable (FR-038).
- Getter order follows closure order (roots in attribute order, then discovered types), as today.

### Closed generic object reached through a property (FR-033)

```csharp
public sealed record Page<T>(IReadOnlyList<T> Items);

public sealed record Feed(Page<EpisodeSummary> Latest);

[JsonSerializable(typeof(Feed))]
public partial class FeedJsonContext : JsonSerializerContext;
```

```ts
get feed(): TypeSpec<Feed> { /* latest: { kind: "ref", spec: () => this.pageEpisodeSummary } */ }

// Added by the closure; today this getter is missing and `this.page` fails with TS2339.
get pageEpisodeSummary(): TypeSpec<Page<EpisodeSummary>> { /* items: array of ref → this.episodeSummary */ }

get episodeSummary(): TypeSpec<EpisodeSummary> { /* … */ }
```

## 3. Branded factory (#269)

```csharp
[Branded]
public readonly record struct Title
{
    private Title(string value) => Value = value;

    public string Value { get; }

    [JsonFactory]
    public static Title From(string value)
    {
        var trimmed = value.Trim();

        if (trimmed.Length == 0)
            throw new ArgumentException("blank");

        return new Title(trimmed);
    }
}
```

```ts
// in any spec property / value descriptor typed Title:
type: { kind: "branded", create: Title.from },
// without [JsonFactory] (unchanged): { kind: "branded", create: Title.create }
// with [JsonFactory, Name("parse")]:  { kind: "branded", create: Title.parse }
// nested branded type Episode.Title:   { kind: "branded", create: Episode.Title.from }
// as a map key:                        { kind: "map", key: { kind: "branded", create: Title.from }, … }
```

The member name after `Title.` is produced by `IrToTsNamingPolicy.ToFunctionName(method.Name,
method.Attributes)`, the function `IrToTsBrandedBridge` uses to declare the companion function, so
the reference and the declaration cannot drift. A factory whose TS name would be `create` or
`toString` is `MS0034`.
