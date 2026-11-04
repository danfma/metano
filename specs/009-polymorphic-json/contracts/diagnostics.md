# Contract: diagnostics

Codes `MS0032`–`MS0037` are reserved by this spec (DECIDED-2); the C# unions specification
(PR #310) owns `MS0038`–`MS0043`. The reservation is listed under "Reserved codes" in
`specs/001-project-baseline-evolution/baseline/diagnostic-catalog.md`. Each code moves to the
stable table, and into `src/Metano.Compiler/Diagnostics/MetaSharpDiagnostic.cs`, with the slice
that implements it.
All are raised by the TypeScript target while transforming a `JsonSerializerContext` (or, for
`MS0034`, by a dedicated validation pass over all transpilable types, so a cache hit on a type's
file cannot suppress it), located at the offending attribute, member or
method.

Warning cases (`MS0033`, `MS0036` warning form, `MS0037`) are tested with inline TUnit
compilations only and never placed in `samples/SampleJsonContracts`, which builds under
`TreatWarningsAsErrors` (plan.md "CI changes").

## `MS0032` `InvalidJsonPolymorphism` — Error

| Rule | Example | Message shape |
|---|---|---|
| Derived type not assignable to the base | `[JsonDerivedType(typeof(Other), "x")]` on `Clip` | `'Other' registered by [JsonDerivedType] on 'Clip' is not assignable to 'Clip'.` |
| *(not an error)* the base registers itself | `[JsonDerivedType(typeof(SBase), "b")]` on `SBase` | — (STJ-valid; FR-004, FR-005) |
| Derived type not transpilable for TypeScript (in its declaring assembly, FR-010) | `[Ignore]` / `[NoTranspile]` derived type, or a type of a non-Metano assembly | `… is not transpiled, so it has no TypeScript contract.` |
| Abstract derived type without `FallBackToNearestAncestor` | `[JsonDerivedType(typeof(AMid))]`, `AMid` abstract | `'AMid' is abstract; abstract derived types require UnknownDerivedTypeHandling = FallBackToNearestAncestor on 'ABase' (as in System.Text.Json).` |
| Duplicate derived type | two entries for `ImageClip` | `'ImageClip' is registered twice on 'Clip'.` |
| Duplicate tag | `"image"` used twice | `Type discriminator 'image' is registered twice on 'Clip'.` |
| *(not an error)* an interface or `[PlainObject]` base, `[PlainObject]` variants | `[JsonDerivedType]` on `IShape` | — (FR-018, FR-019) |
| Interface-typed derived entry (OPEN-18) | `[JsonDerivedType(typeof(IRound))]` on `IShape`, `IRound : IShape` | `'IRound' registered on 'IShape' is an interface; interface-typed derived types are not supported by the TypeScript context.` |
| Two `[PlainObject]` variants cannot be told apart on write (FR-019) | `A(string Name)` and `B(string Name)` on one base | `[PlainObject] variants 'A' and 'B' of 'Base' accept each other's values (required members: name: string), so serialization cannot tell them apart. Add a distinguishing required member or use class-backed records.` |
| A `[PlainObject]` variant's guard accepts every value of the concrete, non-self-registered base (FR-019 rules 4–5, S6c review) | `Dog2(string Name, int? Bark) : Animal2(Name)` | `[PlainObject] variant 'Dog2' of 'Animal2' accepts every 'Animal2' value (required members: name: string), so serialization would write each 'Animal2' as 'Dog2'. Add a distinguishing required member to 'Dog2' or use class-backed records.` |
| A registered `[PlainObject]` type gets no TypeScript guard (S6c review) | `[PlainObject, Import("NE", "ext-lib")] record NE` | `[PlainObject] type 'NE' registered on 'NB' gets no TypeScript guard (for example [Import], [ExportedAsModule], [NoContainer], an exception or a type with extension members), so serialization cannot select it.` (a base without its `baseTest` guard: `[PlainObject] base 'NB' gets no TypeScript guard (…), so serialization cannot tell its own values from unregistered ones.`) |
| Cycle in the `[PlainObject]` specificity order (FR-019) | three variants whose guards cover each other pairwise in a cycle | `[PlainObject] variants 'A', 'B' and 'C' of 'Base' have no consistent test order.` |
| Generic derived type | `typeof(Box<int>)` | `Generic derived type 'Box<int>' is not supported.` |
| Registration invalid for a constructed base reached by the closure (FR-015) | `GDer : GBase<int>` reached through `GBase<string>` | `'GDer' registered on 'GBase<T>' is not assignable to 'GBase<string>', which this context reaches.` |
| Property name equals discriminator, in a contract written with a tag (a tagged registration, inherited properties included; never an untagged one, S6 review) | `KA(string Kind)` under camelCase, tag `kind` | `'KA.Kind' serializes as 'kind', which conflicts with the discriminator of 'KBase'.` |
| Integer-like JSON property name in a contract written with a tag | `[JsonPropertyName("1")]` | `JSON property '1' of '…' is integer-like; JavaScript would order it before the discriminator.` |
| Sealed base (S6 review; a struct cannot carry the attributes, CS0592) | `[JsonDerivedType(typeof(SealedB), "s")] sealed record SealedB` | `'SealedB' does not support polymorphism: polymorphic types cannot be sealed types (as in System.Text.Json).` |
| `[JsonPolymorphic]` without any `[JsonDerivedType]` (S6 review) | `[JsonPolymorphic] record PolyOnly` | `Polymorphic configuration for type 'PolyOnly' should specify at least one derived type (as in System.Text.Json).` |
| Reserved discriminator `$id` / `$ref` / `$values` (S6 review) | `TypeDiscriminatorPropertyName = "$id"` | `The discriminator '$id' of 'IdDisc' is reserved: '$id', '$ref' and '$values' cannot be used as custom type discriminator property names (as in System.Text.Json).` |

The base rules (sealed, no registration, reserved discriminator) and the property rules leave the
base without `polymorphism`; a registration rule leaves out that registration. Messages name a
generic base by its definition (`GBase<T>`), so a diagnostic every construction shares is reported
once.

## `MS0033` `LossyJsonPolymorphism` — Warning

| Rule | Message shape |
|---|---|
| Untagged registration | `'NA' is registered on 'NBase' without a type discriminator; it serializes without a tag and deserializes as 'NBase' (as in System.Text.Json).` — on an abstract base: `… and cannot be deserialized through the abstract 'UAB' (as in System.Text.Json).` |
| Derived type is itself polymorphic | `'NMid' declares its own [JsonDerivedType]s; as in System.Text.Json, they apply only through 'NMid' and are not re-dispatched through 'NBase'.` |

## `MS0034` `InvalidJsonFactory` — Error

| Rule | Message shape |
|---|---|
| Instance method | `[JsonFactory] method 'Title.From' must be static.` |
| Operator or conversion operator | `[JsonFactory] cannot be applied to operator 'Title.op_Explicit'; use a named static method.` |
| Containing type not `[Branded]` / `[InlineWrapper]` | `[JsonFactory] is only valid on a [Branded] type; 'Foo' is not branded.` |
| Wrong arity or parameter type | `[JsonFactory] 'Title.From' must take exactly one 'string' parameter.` |
| Wrong return type | `[JsonFactory] 'Title.From' must return 'Title'.` |
| More than one factory | `'Title' declares more than one [JsonFactory] method.` |
| Generic factory | `[JsonFactory] 'Title.From' must not be generic.` |
| Overloaded factory | `[JsonFactory] 'Title.From' is overloaded; the TypeScript dispatcher could call a different overload.` |
| Another method emitted under the factory's TS name (`[Name]` rename) | `[JsonFactory] 'Title.From' emits as 'from', which 'Title.Other' also uses; rename one or add [Name].` |
| Non-public factory in an `[EmitPackage]` library | `[JsonFactory] 'Title.From' must be public in a package library; consumers compiled against its metadata cannot see it.` |
| Not emitted (`[Ignore]`, `[Emit]`, `[Import]`, `[Inline]`) | `[JsonFactory] 'Title.From' is not emitted to TypeScript.` |
| TS name equals a synthesized companion member | `[JsonFactory] 'Title.Create' emits as 'create', which the branded companion already declares; rename it or add [Name].` (also `toString`) |

When `MS0034` is raised the descriptor keeps `create: <TsType>.create`.

## `MS0035` `InvalidJsonSerializableRoot` — Error

| Rule | Example |
|---|---|
| Open generic / type parameter | `typeof(List<>)` |
| Error type (unresolvable) | `typeof(Missing)` |
| `object` / `dynamic` | `typeof(object)` |
| Non-collection interface without `[JsonDerivedType]` (a polymorphic interface is a root, FR-018) | `typeof(IShape)` |
| Object-kind root that is not a transpilable class/struct | `typeof(KeyValuePair<string, int>)`, `typeof((int, string))`, a `[NoTranspile]` type, a type from a non-Metano assembly |
| Array of rank > 1 | `typeof(int[,])` |
| Dictionary with a non-`string` key anywhere in a value root (until S4 fixes key conversion, #270) | `typeof(Dictionary<int, Leaf>)`, `typeof(List<Dictionary<Guid, Leaf>>)` |
| Value root whose element / value / nullable-inner object type has no object contract (an interface, or a type that is not transpiled in its declaring assembly) | `typeof(List<System.Version>)`, `typeof(List<IShape>)` |
| Closed generic object root (or closed generic element of a value root) with a type argument that has no object contract (DECIDED-19 g) | `typeof(Page<System.Version>)`, `typeof(Page<IShape>)` |

Message shape: `'<type>' cannot be a [JsonSerializable] root: <reason>.` The root is not emitted.

## `MS0036` `JsonTypeInfoNameCollision` — Error / Warning

| Rule | Severity | Message shape |
|---|---|---|
| Two distinct types map to one member name; the loser is the target of a `ref`, `base` or derived link | Error | `'Anchor.Image' and 'Clip.Image' both map to context member 'image'; 'Anchor.Image' is referenced by 'Anchor.Overlay', so it cannot be dropped. Use TypeInfoPropertyName to resolve the collision.` |
| A member name equals a `SerializerContext` member (`options`, `converters`, `createSpec`, `createValueSpec`, `readOptions`, `resolve`, `_registry`, `constructor`); the generated static `default` / `_default` do not clash with instance members and are not reserved (#270 decision (e)) | Error | `'Registry' maps to context field '_registry', which SerializerContext already declares. Use TypeInfoPropertyName.` |
| Two distinct roots map to one member name; the loser is not referenced | Warning | `'N2.Dup' and 'N1.Dup' both map to context member 'dup'; 'N1.Dup' was kept. Use TypeInfoPropertyName to resolve the collision.` |
| One type listed twice with different `TypeInfoPropertyName` values (#270) | Warning | `'System.Collections.Generic.List<string>' is listed again with TypeInfoPropertyName 'Labels'; the first TypeInfoPropertyName 'Names' was kept.` |

Losers are never emitted. Type names in messages use the C# error-message format (namespace
included, e.g. `TestApp.Anchor.Image`).

## `MS0037` `UnmirroredJsonOption` — Warning

Every writable property of `JsonSourceGenerationOptionsAttribute` and every attribute of
`System.Text.Json.Serialization` on .NET 10 is either mirrored, ignored because it does not change
the JSON value, or reported here (FR-046, DECIDED-25).

| Rule | Location | Message shape |
|---|---|---|
| Context sets an unmirrored option to a value other than STJ's default: `DefaultIgnoreCondition`, `IgnoreReadOnlyProperties`, `IgnoreReadOnlyFields`, `IncludeFields`, `UseStringEnumConverter`, `Converters` (non-empty), `ReferenceHandler`, `DictionaryKeyPolicy`, `ReadCommentHandling`, `AllowTrailingCommas`, `AllowDuplicateProperties`, `UnmappedMemberHandling`, `UnknownTypeHandling`, `PreferredObjectCreationHandling`, `RespectNullableAnnotations`, `RespectRequiredConstructorParameters`, `MaxDepth` (other than `0` / `64`) | the named argument | `'DefaultIgnoreCondition' changes the JSON wire format, but the generated TypeScript context does not mirror it.` |
| `NumberHandling` with a flag other than `AllowReadingFromString` (one warning per flag) | the named argument | `'NumberHandling.WriteAsString' changes the JSON wire format, …` |
| `[JsonInclude]` on a member the source generator cannot reach (`private`, `protected`, `private protected`, or a public property with such an accessor; SYSLIB1038, the STJ contract throws `InvalidOperationException`) | member | `'Inc.Secret' has [JsonInclude], but the System.Text.Json source generator cannot access it (SYSLIB1038), so its contract throws InvalidOperationException; the TypeScript context does not mirror that.` |
| `[JsonExtensionData]` on a serialized member (left out of the spec) | attribute | `'Open.Extra' is [JsonExtensionData]: System.Text.Json writes its entries as properties of the object and collects unmapped ones into it, the TypeScript context leaves it out.` |
| `[JsonConverter]` on a serialized member | attribute | `'Doc.Title' has [JsonConverter]: the TypeScript context converts it as its type, without the custom converter.` |
| `[JsonConverter]` on a type the context reaches, except a `[Branded]` / `[InlineWrapper]` type | attribute | `'Mood' has [JsonConverter]: the TypeScript context converts it with its built-in descriptor, without the custom converter.` |
| `[JsonNumberHandling]` on a serialized member / on a contract type | attribute | `'Stats.Count' has [JsonNumberHandling]: the TypeScript context applies the context's number handling.` / `'Stats' has [JsonNumberHandling]: … to its members.` |
| `[JsonObjectCreationHandling(Populate)]` on a serialized member or a contract type | attribute | `'Bag.Items' has [JsonObjectCreationHandling(Populate)]: the TypeScript context replaces the value on read.` |
| `[JsonUnmappedMemberHandling(Disallow)]` on a contract type | attribute | `'Strict' has [JsonUnmappedMemberHandling(Disallow)]: the TypeScript context skips unmapped JSON properties.` |
| `[JsonRequired]` on a nullable serialized member | attribute | `'Req.Note' has [JsonRequired]: System.Text.Json rejects a payload without it, the TypeScript context reads a missing nullable member as null.` |
| `[JsonIgnore(Condition = WhenWritingNull \| WhenWritingDefault \| WhenWriting \| WhenReading)]` (the member is kept) | member | `'Flags.Note' has [JsonIgnore(Condition = WhenWritingNull)]: the TypeScript context always writes and reads it.` |
| `[JsonConstructor]` on a constructor other than the one the factory calls | constructor | `'Pair' has [JsonConstructor] on a constructor the TypeScript context does not call: it constructs the type through its primary constructor.` |

Each member and type is reported once per context (a generic definition once for all its
constructions); attributes of a member System.Text.Json does not serialize are not reported.

Not reported, because they are mirrored: `PropertyNamingPolicy`, `PropertyNameCaseInsensitive`,
`NumberHandling = AllowReadingFromString`, `JsonSerializerDefaults.Web` and
`AllowOutOfOrderMetadataProperties` (FR-002, FR-048); `[JsonPropertyName]`,
`[JsonPropertyOrder]`, `[JsonInclude]` on a reachable member, `byte[]` (base64),
`[JsonIgnore]` with `Always` / `Never`, `[JsonRequired]` on a non-nullable member,
`[JsonConstructor]` on the constructor the factory calls, `[JsonPolymorphic]`,
`[JsonDerivedType]`, `[JsonSerializable]` (S6b, DECIDED-25). Not reported, because they do not
change the JSON value: `WriteIndented`, `IndentCharacter`, `IndentSize`, `NewLine`,
`DefaultBufferSize`, `GenerationMode`. `[JsonStringEnumMemberName]` only matters under a string
enum converter, which is reported itself.
