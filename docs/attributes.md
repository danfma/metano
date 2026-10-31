# Attribute Reference

Most Metano attributes live in the `Metano.Annotations` namespace. Import it
once per file:

```csharp
using Metano.Annotations;
```

TypeScript-only attributes live in `Metano.Annotations.TypeScript`:

```csharp
using Metano.Annotations.TypeScript;
```

This page is the complete reference — every attribute, what it does, and when to
use it.

## Type selection

### `[Transpile]`

Marks a single type for transpilation. Without this (and without
`[assembly: TranspileAssembly]`), a type is **ignored** even if it's public.

```csharp
[Transpile]
public record User(string Name, string Email);
```

### `[assembly: TranspileAssembly]`

Assembly-level attribute. Transpiles **every public type** in the assembly by
default. Individual types can opt out with `[Ignore]`.

```csharp
[assembly: TranspileAssembly]
```

Use this when most of your types should cross to TypeScript — less boilerplate
than marking each type individually.

## Naming

### `[Name("x")]`

Overrides the name emitted in the TypeScript output. Works on types, methods,
properties, fields, and enum members.

```csharp
[Name("USD")] USDollar,

[Name("api")]
public void CallApi() { /* … */ }

[Name("snake_case_name")]
public string OriginalCamelCase { get; set; }
```

### `[Ignore]`

Marks a type or member as **.NET-only**. Ignored symbols do not emit for the
selected target, and transpilable code may not reference ignored types in
signatures or bodies. If a generated surface mentions an ignored type, the
compiler reports `MS0013`.

Use `[Ignore]` for implementation-only C# types or members that should stay out
of generated targets. For ambient JavaScript/runtime declarations, prefer the
TypeScript-only `[External]` attribute or `[Import]`, depending on whether the
symbol comes from a global/runtime surface or an npm package.

```csharp
[Ignore]
public sealed class EfCoreOnlyProjection { }

public class User
{
    public string Name { get; set; }

    [Ignore]
    public string InternalDebugInfo { get; set; } // not emitted
}
```

The attribute is target-aware:

```csharp
[Ignore(TargetLanguage.Dart)]
public sealed class TypeScriptOnlyWidget { }
```

The parameterless form applies to every target. `[Ignore(TargetLanguage.Dart)]`
suppresses only the Dart backend while leaving TypeScript emission unchanged.

## Output shaping

### `[StringEnum]`

Emits the enum as a **string union** (via a `const` object + `type` alias)
instead of a numeric TypeScript `enum`. Recommended for most enums — more
idiomatic in TypeScript and plays nicely with JSON.

```csharp
[StringEnum]
public enum Priority
{
    Low,
    [Name("medium")] Medium,
    High,
}
```

Becomes:

```typescript
export const Priority = {
  Low: "Low",
  Medium: "medium",
  High: "High",
} as const;
export type Priority = (typeof Priority)[keyof typeof Priority];
```

### `[Branded]` — branded primitives

Struct with a single primitive field → **branded type** with zero runtime overhead.
Perfect for strongly-typed IDs.

```csharp
[Branded]
public readonly record struct UserId(string Value)
{
    public static UserId New() => new(Guid.NewGuid().ToString("N"));
}
```

Becomes:

```typescript
export type UserId = string & { readonly __brand: "UserId" };
export namespace UserId {
  export function create(value: string): UserId { return value as UserId; }
  export function newId(): UserId { return crypto.randomUUID().replace(/-/g, "") as UserId; }
}
```

At runtime `UserId` is literally a `string` — no wrapper object. But the type
system prevents you from passing a plain string where a `UserId` is expected.

`[InlineWrapper]` is still supported with the same behavior for existing code,
but new code should prefer `[Branded]`.

### `[PlainObject]` — record → interface

Emits a record or class as a TypeScript **interface** instead of a class. No
`equals`, no `hashCode`, no `with`, no constructor call site lowering.

Useful for DTOs that flow through `JSON.stringify`/`parse` or HTTP boundaries.

```csharp
[PlainObject]
public record CreateUserDto(string Name, string Email);
```

Becomes:

```typescript
export interface CreateUserDto {
  name: string;
  email: string;
}
```

And at call sites, `new CreateUserDto("Alice", "a@example.com")` becomes
`{ name: "Alice", email: "a@example.com" }`.

### `[NoContainer]`

Applied to a **static class**: instead of emitting a class, each method becomes a
top-level exported function in the generated target module, and member access
drops the class qualifier at call sites.

```csharp
[NoContainer]
public static class MathHelpers
{
    public static int Square(int x) => x * x;
    public static int Cube(int x) => x * x * x;
}
```

Becomes:

```typescript
export function square(x: number): number {
  return x * x;
}

export function cube(x: number): number {
  return x * x * x;
}
```

No `MathHelpers` class in the output — just loose functions. Idiomatic in JS
modules.

`[ExportedAsModule]` is obsolete and kept temporarily for existing callers.
New code should use `[NoContainer]`. `[Module]` is accepted as an alias for the
same module-shaping intent, but `[NoContainer]` is the clearest spelling when
the class is purely a compile-time container.

### `[ObjectArgs]`

Lowers a method, constructor, or class API so the TypeScript surface receives a
single object argument. Each C# parameter becomes a property on the object type;
optional C# parameters become optional properties.

This is useful for UI and interop APIs that conventionally take props objects.

```csharp
[ObjectArgs]
public static Widget Column(int gap = 0, Widget[] children) =>
    new ColumnWidget(gap, children);
```

A call such as `Column(gap: 12, children: items)` emits as:

```typescript
column({ gap: 12, children: items })
```

### `[GenerateGuard]`

Generates a TypeScript type guard function `isTypeName(value: unknown): value is TypeName`
next to the type. Validates structure at runtime.

```csharp
[Transpile, GenerateGuard]
public record Money(decimal Amount, string Currency);
```

Emits both the `Money` class AND:

```typescript
export function isMoney(value: unknown): value is Money {
  return (
    value instanceof Money ||
    (typeof value === "object" && value !== null &&
      "amount" in value && value.amount instanceof Decimal &&
      "currency" in value && typeof value.currency === "string")
  );
}
```

Guards are auto-imported wherever they're needed (e.g., in generated
overload dispatchers).

### `[EmitInFile("name")]`

Co-locates multiple types in a single `.ts` file. Types in the same C# namespace
with the same `EmitInFile("foo")` name share `foo.ts`.

```csharp
[PlainObject, EmitInFile("todos")]
public record StoredTodo(string Id, TodoItem Item);

[PlainObject, EmitInFile("todos")]
public record CreateTodoDto(string Title, Priority Priority);

[EmitInFile("todos")]
public class TodoStore { /* … */ }
```

All three land in `todos.ts`. Consumers that import any of them get
`import { … } from "…/todos"` automatically.

### `[Inline]`

Marks a static readonly field, static expression-bodied property, or static
single-expression method for use-site inlining. The declaration itself does not
emit as a top-level export.

```csharp
[Inline]
public static int Twice(int value) => value * 2;
```

`[Inline(InlineMode.Substitute)]` performs direct parameter substitution.
The default `InlineMode.Materialize` emits a small inline function shape so
arguments are evaluated once.

### `[Constant]`

Requires a field or call-site argument to be known at compile time. Invalid
uses report `MS0014`.

```csharp
public static HtmlTag Tag([Constant] string name) =>
    throw new NotSupportedException();

[Constant]
public const string Section = "section";
```

## Module-level features

### `[ModuleEntryPoint]`

Applied to a static method. The method **body** becomes top-level code in the
generated TypeScript module — no function wrapper. Useful for app bootstrap code.

```csharp
[NoContainer]
public static class Program
{
    [ModuleEntryPoint]
    public static void Main()
    {
        var app = new Hono();
        app.Get("/", c => c.Text("hello"));
    }
}
```

Becomes:

```typescript
const app = new Hono();
app.get("/", (c) => c.text("hello"));
```

### `[ExportVarFromBody("name", AsDefault = true)]`

Combines with `[ModuleEntryPoint]` to promote a local variable to a module
export (optionally `export default`).

```csharp
[NoContainer]
public static class Program
{
    [ModuleEntryPoint, ExportVarFromBody("app", AsDefault = true)]
    public static void Main()
    {
        var app = new Hono();
        // … wire routes
    }
}
```

Becomes:

```typescript
const app = new Hono();
// … routes

export default app;
```

## Delegates and call-site binding

### `[This]`

Marks the first parameter of a delegate or inlinable method as the JavaScript
`this` receiver. The parameter is removed from the emitted argument list and
reintroduced as a TypeScript `this:` annotation.

```csharp
public delegate void MouseListener([This] Element self, MouseEvent ev);
```

Becomes:

```typescript
export type MouseListener = (this: Element, ev: MouseEvent) => void;
```

The attribute is target-agnostic, but only targets with JS-style rebinding use
it today. Invalid placement reports `MS0018`.

## External bindings

### `[Import(name, from, Version = "^x.y.z", AsDefault = false)]`

Declares a type that's provided by an **external npm package**, not by your C#
code. The transpiler emits `import { Name } from "package-name"` wherever this
type is used, and adds the package to the generated `package.json#dependencies`.

```csharp
[Import(name: "Hono", from: "hono", Version = "^4.6.0")]
public class Hono
{
    [Name("get")]
    public void Get(string path, Func<IHonoContext, IHonoContext> handler) =>
        throw new NotSupportedException();
}
```

Set `AsDefault = true` for default imports (`import Foo from "package"`).

### `[assembly: ExportFromBcl(typeof(BclType), FromPackage = "npm-pkg", ExportedName = "TsName", Version = "^x.y.z")]`

Assembly-level mapping from a .NET BCL type to an npm package. Used by the
`Metano` package itself to wire up default mappings like
`decimal` → `Decimal` from `decimal.js`.

```csharp
[assembly: ExportFromBcl(
    typeof(decimal),
    FromPackage = "decimal.js",
    ExportedName = "Decimal",
    Version = "^10.6.0")]
```

### `[Emit("$0.foo($1, $2)")]`

Inlines JavaScript at the call site, with `$0` = the receiver and `$1, $2, …` =
arguments. Escape hatch for when you need a specific JS idiom that isn't worth
modeling in C#.

```csharp
[Emit("$0.replace(/-/g, \"\")")]
public static string StripDashes(this string value) =>
    throw new NotSupportedException();
```

At the call site `text.StripDashes()` emits `text.replace(/-/g, "")`.

### `[ImportAlias]`

Pins the local name used for an imported type in the generated file. Declare it
on a C# file-scoped carrier class.

```csharp
[ImportAlias(typeof(Column), "ColumnWidget", Target = TargetLanguage.TypeScript)]
file class TsAliases;
```

Bulk aliasing is also supported:

```csharp
[ImportAlias(Suffix = "Widget", Types = [typeof(Row), typeof(Text)])]
file class TsAliases;
```

Precedence is `[ImportAlias]` > C# `using X = Y` > automatic collision aliasing
> canonical name.

## TypeScript-specific attributes

These attributes require:

```csharp
using Metano.Annotations.TypeScript;
```

### `[Optional]`

Marks a nullable parameter or property as optional-presence in TypeScript:
`name?: T | null` instead of the default present nullable value
`name: T | null`.

```csharp
public record SearchRequest([Optional] string? Query);
```

The C# type must be nullable. Applying `[Optional]` to a non-nullable type
reports `MS0010`.

### `[External]`

Declares a TypeScript runtime-provided symbol. External symbols emit no `.ts`
file, but transpilable code may still reference them. This is the right choice
for DOM shapes, Hono-style context interfaces, abstract ambient classes, and
runtime globals provided by another declaration file or package.

`[External]` accepts static classes, abstract classes, interfaces, structs, and
members. Concrete non-static classes are rejected with `MS0012`, because Metano
cannot both erase the declaration and preserve instance implementation.

Static member access stays class-qualified:

```csharp
[External]
public static class Js
{
    [Name("document")]
    public static Document Document => throw new NotSupportedException();
}
```

`Js.Document` emits as `Js.document`. Add `[NoContainer]` to the same static
class when you intentionally want the qualifier erased.

Use `[External]` for ambient/runtime symbols. Use `[Import]` for npm package
symbols.

### `[Discriminator("FieldName")]`

Marks the discriminator field for generated TypeScript guards. The field must
exist, be non-nullable, and use a `[StringEnum]` type.

```csharp
[GenerateGuard]
[Discriminator(nameof(Kind))]
public interface Shape
{
    ShapeKind Kind { get; }
}
```

Invalid discriminator setup reports `MS0011`.

## Cross-project packaging

### `[assembly: EmitPackage("name", Version = "^x.y.z")]`

Declares the npm package name for this assembly's transpiled output. Used by
cross-project resolution: when another project references this assembly, imports
resolve to `"name/subpath"` instead of `"#/...".

```csharp
[assembly: TranspileAssembly]
[assembly: EmitPackage("@acme/domain", Version = "^1.0.0")]
```

The `Version` field is optional — if omitted, the consumer uses `workspace:*` for
monorepo siblings or the assembly's `Identity.Version`.

The optional `SubpathRoot` names the namespace prefix the package name stands for.
It is stripped from the `package.json#exports` keys and from consumers' imports
(on-disk layout and the `#/*` alias are unchanged):

```csharp
[assembly: EmitPackage("@video-studio/domain", SubpathRoot = "VideoStudio.Domain")]
// VideoStudio.Domain.Episodes → import { Episode } from "@video-studio/domain/episodes"
// VideoStudio.Domain          → import { Clock } from "@video-studio/domain"
```

Matching is by whole namespace segment (`VideoStudio.DomainEvents` keeps its full
path). An invalid value reports `MS0029`; two barrels claiming the same key (including the
`--namespace-barrels` root barrel vs. types in the root namespace, both on `.`) report
`MS0030`. See
[Cross-Project References](cross-package.md#shortening-package-subpaths-with-subpathroot).

## Declarative BCL mappings

### `[assembly: MapMethod(typeof(T), "MethodName", JsMethod = "jsName")]`

Maps a .NET BCL method to a JS method. Used by the `Metano` assembly to
define things like `List<T>.Add` → `push`, `List<T>.Contains` → `includes`.

```csharp
[assembly: MapMethod(typeof(List<>), nameof(List<int>.Add), JsMethod = "push")]
```

Alternative: `JsTemplate` for full control:

```csharp
[assembly: MapMethod(
    typeof(Enumerable),
    nameof(Enumerable.Where),
    WrapReceiver = "Enumerable.from",
    JsTemplate = "$this.where($0)")]
```

Placeholders:

- `$this` — the receiver
- `$0, $1, …` — arguments
- `$T0, $T1, …` — generic type arguments

### `[assembly: MapProperty(typeof(T), "PropertyName", JsProperty = "jsName")]`

Same idea for properties.

```csharp
[assembly: MapProperty(typeof(List<>), nameof(List<int>.Count), JsProperty = "length")]
```

## See also

- [BCL Type Mappings](bcl-mappings.md) — how C# types map to TypeScript
- [Cross-Project References](cross-package.md) — deeper dive into `[EmitPackage]`
