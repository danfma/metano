# Contract: diagnostics

Codes are reserved by this spec (OPEN-2) and added to
`src/Metano.Compiler/Diagnostics/MetaSharpDiagnostic.cs` and
`specs/001-project-baseline-evolution/baseline/diagnostic-catalog.md` with the implementation.
All are raised by the TypeScript target while transforming a `JsonSerializerContext` (or, for
`MS0034`, while transforming the branded type), located at the offending attribute, member or
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
| Interface or `[PlainObject]` base or variant | `[JsonDerivedType]` on `IShape` | `Polymorphic JSON needs a class base and class variants; '…' is an interface/[PlainObject].` |
| Generic derived type | `typeof(Box<int>)` | `Generic derived type 'Box<int>' is not supported.` |
| Registration invalid for a constructed base reached by the closure (FR-015) | `GDer : GBase<int>` reached through `GBase<string>` | `'GDer' registered on 'GBase<T>' is not assignable to 'GBase<string>', which this context reaches.` |
| Property name equals discriminator | `KA(string Kind)` under camelCase, tag `kind` | `'KA.Kind' serializes as 'kind', which conflicts with the discriminator of 'KBase'.` |
| Integer-like JSON property name in the hierarchy | `[JsonPropertyName("1")]` | `JSON property '1' of '…' is integer-like; JavaScript would order it before the discriminator.` |

## `MS0033` `LossyJsonPolymorphism` — Warning

| Rule | Message shape |
|---|---|
| Untagged registration | `'NA' is registered on 'NBase' without a type discriminator; it serializes without a tag and deserializes as 'NBase' (as in System.Text.Json).` |
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
| Overloaded factory | `[JsonFactory] 'Title.From' is overloaded; the TypeScript dispatcher could call a different overload.` |
| Not emitted (`[Ignore]`, `[Emit]`, `[Import]`, `[Inline]`) | `[JsonFactory] 'Title.From' is not emitted to TypeScript.` |
| TS name equals a synthesized companion member | `[JsonFactory] 'Title.Create' emits as 'create', which the branded companion already declares; rename it or add [Name].` (also `toString`) |

When `MS0034` is raised the descriptor keeps `create: <TsType>.create`.

## `MS0035` `InvalidJsonSerializableRoot` — Error

| Rule | Example |
|---|---|
| Open generic / type parameter | `typeof(List<>)` |
| Error type (unresolvable) | `typeof(Missing)` |
| `object` / `dynamic` | `typeof(object)` |
| Non-collection interface | `typeof(IShape)` |
| Object-kind root that is not a transpilable class/struct | `typeof(KeyValuePair<string, int>)`, `typeof((int, string))`, a `[NoTranspile]` type, a type from a non-Metano assembly |
| Array of rank > 1 | `typeof(int[,])` |

## `MS0036` `JsonTypeInfoNameCollision` — Error / Warning

| Rule | Severity | Message shape |
|---|---|---|
| Two distinct types map to one member name; the loser is the target of a `ref`, `base` or derived link | Error | `'Anchor.Image' and 'Clip.Image' both map to context member 'image'; 'Anchor.Image' is referenced by 'Anchor.Overlay', so it cannot be dropped. Use TypeInfoPropertyName to resolve the collision.` |
| A member name equals a `SerializerContext` member (`options`, `converters`, `createSpec`, `resolve`, `_registry`, `constructor`) | Error | `'Registry' maps to context field '_registry', which SerializerContext already declares. Use TypeInfoPropertyName.` |
| Two distinct roots map to one member name; the loser is not referenced | Warning | `'N2.Dup' and 'N1.Dup' both map to context member 'dup'; 'N1.Dup' was kept. Use TypeInfoPropertyName to resolve the collision.` |

## `MS0037` `UnmirroredJsonOption` — Warning

| Rule | Location | Message shape |
|---|---|---|
| Context sets a wire-affecting option that is not mirrored (FR-046 list) | `[JsonSourceGenerationOptions]` | `'DefaultIgnoreCondition' changes the JSON wire format, but the generated TypeScript context does not mirror it.` |
| `[JsonInclude]` field or non-public property | member | `'Episode._secret' is included by System.Text.Json but not collected by the TypeScript context.` |
| `byte[]` root or property | attribute / member | `'Blob.Data' is byte[]: System.Text.Json writes base64, the TypeScript context writes a number array.` |
