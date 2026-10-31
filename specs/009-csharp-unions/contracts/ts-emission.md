# Contract: TypeScript emission for unions

**Normative for**: FR-009, FR-010, FR-017–FR-022; D1–D4.

All examples assume the following C# source (namespace `Studio.Clips`, `[assembly: TranspileAssembly]`, `LangVersion preview`, net11.0):

```csharp
public record ImageClip(string Path, double Seconds);
public record SilenceClip(double Seconds);

public union Clip(ImageClip, SilenceClip);
public union Id(int, string);
public union NId(int?, string);

public record None;
public union Opt<T>(T, None) where T : notnull;

[ExportedAsModule]
public static class Clips
{
    public static string Describe(Clip c) => c switch
    {
        ImageClip i => i.Path,
        SilenceClip s => $"{s.Seconds}s",
    };

    public static string Show(Id id) => id switch
    {
        int n => $"#{n}",
        string s => s,
    };

    public static string ShowOpt(Clip? c) => c switch
    {
        ImageClip i => i.Path,
        SilenceClip => "silence",
        null => "none",
    };

    public static Clip Make() => new SilenceClip(2);              // implicit case→union conversion
    public static Clip Wrap(ImageClip i) => new Clip(i);          // union constructor
    public static bool IsImage(Clip c) => c.Value is ImageClip;   // .Value
    public static bool Same(Clip a, Clip b) => a.Equals(b);       // value equality
}
```

## Declarations (FR-017)

```ts
// studio/clips/clip.ts
import type { ImageClip } from "./image-clip";
import type { SilenceClip } from "./silence-clip";

export type Clip = ImageClip | SilenceClip;
```

```ts
// studio/clips/id.ts
export type Id = number | string;
```

```ts
// studio/clips/n-id.ts
export type NId = number | null | string;
```

```ts
// studio/clips/opt.ts
import type { None } from "./none";

export type Opt<T> = T | None;
```

A union never produces a runtime value. The barrel re-exports it with `export type { Clip } from "./clip";`, matching the existing type-only alias exports.

## Switch expression with a `never` tail (FR-019, FR-020)

```ts
export function describe(c: Clip): string {
  return (($s) => {
    if ($s instanceof ImageClip) {
      const i = $s;
      return i.path;
    }

    if ($s instanceof SilenceClip) {
      const s = $s;
      return `${s.seconds}s`;
    }

    $s satisfies never;
    throw new Error("Non-exhaustive switch expression");
  })(c);
}

export function show(id: Id): string {
  return (($s) => {
    if (typeof $s === "number") {
      const n = $s;
      return `#${n}`;
    }

    if (typeof $s === "string") {
      const s = $s;
      return s;
    }

    $s satisfies never;
    throw new Error("Non-exhaustive switch expression");
  })(id);
}
```

- `ImageClip` and `SilenceClip` are **value** imports in this file, because `instanceof` needs the class. `Clip` and `Id` are type imports.
- `IrRuntimeTypeTest` (PR #298) chooses the primitive test (`typeof` or an `is*` helper). If it picks a helper, the scanner (FR-011) imports it from `metano-runtime`.
- `default(Clip)` reaches the throw at runtime, mirroring C#'s `SwitchExpressionException`.

## Nullable scrutinee (FR-020 condition 4)

```ts
export function showOpt(c: Clip | null): string {
  return (($s) => {
    if ($s instanceof ImageClip) {
      const i = $s;
      return i.path;
    }

    if ($s instanceof SilenceClip) {
      return "silence";
    }

    if ($s === null) {
      return "none";
    }

    $s satisfies never;
    throw new Error("Non-exhaustive switch expression");
  })(c);
}
```

The `null` arm stays in its source position. If the `null =>` arm were removed, the `$s satisfies never;` line would be omitted and only the throw would remain.

## Identity rewrites (FR-009, FR-010)

```ts
export function make(): Clip {
  return new SilenceClip(2);
}

export function wrap(i: ImageClip): Clip {
  return i;
}

export function isImage(c: Clip): boolean {
  return c instanceof ImageClip;
}

export function same(a: Clip, b: Clip): boolean {
  return equals(a, b);   // `equals` comes from metano-runtime, imported the way runtime helpers are imported today
}
```

## Switch statement with pattern labels (FR-021)

```csharp
switch (c)
{
    case ImageClip i:
        Log(i.Path);
        break;
    case SilenceClip s when s.Seconds > 10:
        Log("long");
        break;
    case SilenceClip:
        Log("short");
        break;
}
```

```ts
{
  const $s = c;

  if ($s instanceof ImageClip) {
    const i = $s;
    log(i.path);
  } else if ($s instanceof SilenceClip && $s.seconds > 10) {
    log("long");
  } else if ($s instanceof SilenceClip) {
    log("short");
  }
}
```

A C# switch *statement* is never required to be exhaustive and never throws when nothing matches. The lowering therefore has no `never` check and no throw: an unmatched value falls through silently, as in C#. The `when` clause joins the type test as `test && guard`. When the label has a designator the guard reads, the lowering nests the guard inside the binding block, as switch-expression arms already do.

## Union guard (FR-019)

```csharp
[GenerateGuard] public union Id(int, string);
```

```ts
export type Id = number | string;

export function isId(value: unknown): value is Id {
  return typeof value === "number" || typeof value === "string";
}
```

## Cross-package (FR-022)

```ts
// consumer file
import type { Clip } from "@studio/clips/studio/clips";
import { ImageClip, SilenceClip } from "@studio/clips/studio/clips";
```

The specifiers follow the 005/008 layout rules. Names coming from one module merge into one import line, with a per-name `type` qualifier where the existing merge rules apply.

## Dart (FR-027)

```dart
/// union: ImageClip | SilenceClip
typedef Clip = Object;

/// union: int? | String
typedef NId = Object?;
```

A switch expression over a union ends with `_ => throw StateError('Non-exhaustive switch expression')`.
