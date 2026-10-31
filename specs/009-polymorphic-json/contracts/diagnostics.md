# Contract: diagnostics

Codes are reserved by this spec (OPEN-2) and added to
`src/Metano.Compiler/Diagnostics/MetaSharpDiagnostic.cs` and
`specs/001-project-baseline-evolution/baseline/diagnostic-catalog.md` with the implementation.
All are raised by the TypeScript target while transforming a `JsonSerializerContext` (or, for
`MS0034`, while transforming the branded type), located at the offending attribute or method.

## `MS0032` `InvalidJsonPolymorphism` — Error

| Rule | Example | Message shape |
|---|---|---|
| Derived type is not a proper subtype | `[JsonDerivedType(typeof(Other), "x")]` on `Clip` | `'Other' registered by [JsonDerivedType] on 'Clip' does not derive from 'Clip'.` |
| Derived type not transpilable for TypeScript | `[Ignore]` / `[NoTranspile]` derived type | `… is not transpiled, so it has no TypeScript contract.` |
| Duplicate derived type | two entries for `ImageClip` | `'ImageClip' is registered twice on 'Clip'.` |
| Duplicate tag | `"image"` used twice | `Type discriminator 'image' is registered twice on 'Clip'.` |
| Interface or `[PlainObject]` base or variant | `[JsonDerivedType]` on `IShape` | `Polymorphic JSON needs a class base and class variants; '…' is an interface/[PlainObject].` |
| Generic derived type | `typeof(Box<int>)` | `Generic derived type 'Box<int>' is not supported.` |
| Property name equals discriminator | `KA(string Kind)` under camelCase, tag `kind` | `'KA.Kind' serializes as 'kind', which conflicts with the discriminator of 'KBase'.` |
| Integer-like JSON property name in the hierarchy | `[JsonPropertyName("1")]` | `JSON property '1' of '…' is integer-like; JavaScript would order it before the discriminator.` |
| Polymorphic base from a referenced package whose derived types are not importable | OPEN-10 | `…` |

## `MS0033` `LossyJsonPolymorphism` — Warning

| Rule | Message shape |
|---|---|
| Untagged registration | `'NA' is registered on 'NBase' without a type discriminator; it serializes without a tag and deserializes as 'NBase' (as in System.Text.Json).` |
| Derived type is itself polymorphic | `'HMid' declares its own [JsonDerivedType]s; values reached through 'HBase' are not re-dispatched.` (OPEN-9) |

## `MS0034` `InvalidJsonFactory` — Error

| Rule | Message shape |
|---|---|
| Instance method | `[JsonFactory] method 'Title.From' must be static.` |
| Containing type not `[Branded]` / `[InlineWrapper]` | `[JsonFactory] is only valid on a [Branded] type; 'Foo' is not branded.` |
| Wrong arity or parameter type | `[JsonFactory] 'Title.From' must take exactly one 'string' parameter.` |
| Wrong return type | `[JsonFactory] 'Title.From' must return 'Title'.` |
| More than one factory | `'Title' declares more than one [JsonFactory] method.` |
| Overloaded factory | `[JsonFactory] 'Title.From' is overloaded; the TypeScript dispatcher could call a different overload.` |
| Not emitted (`[Ignore]`, `[Emit]`, `[Import]`, `[Inline]`) | `[JsonFactory] 'Title.From' is not emitted to TypeScript.` |

When `MS0034` is raised the descriptor keeps `create: <TsType>.create`.

## `MS0035` `InvalidJsonSerializableRoot` — Error

| Rule | Example |
|---|---|
| Open generic / type parameter | `typeof(List<>)` |
| Error type (unresolvable) | `typeof(Missing)` |
| `object` / `dynamic` | `typeof(object)` |
| Non-collection interface | `typeof(IShape)` |

## `MS0036` `JsonTypeInfoNameCollision` — Warning

| Rule | Message shape |
|---|---|
| Two distinct types map to one member name | `'N2.Dup' and 'N1.Dup' both map to context member 'dup'; 'N1.Dup' was kept. Use TypeInfoPropertyName to resolve the collision.` |
