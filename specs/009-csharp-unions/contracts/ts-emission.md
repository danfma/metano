# Contract: TypeScript emission for unions

**Normative for**: FR-009, FR-010, FR-017–FR-022, FR-032–FR-035; D1–D4.

Primitive case tests are shown as `typeof`. The final rendering (`typeof` or an `is*` helper) is whatever PR #298 ships, and the goldens are finalized after it merges (FR-019). Union guards are the exception: they always validate integers with the `isInt32` family (FR-035).

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

A C# switch *statement* is never required to be exhaustive and never throws when nothing matches. The lowering therefore has no `never` check and no throw: an unmatched value falls through silently, as in C#. The `when` clause joins the type test as `test && guard`. When the label has a designator the guard reads, the lowering nests the guard inside the binding block, as switch-expression arms already do. Each section's trailing `break;` is dropped, and no `break $switch` remains, so the block is not labeled.

### Mid-section `break`, middle `default`, mixed labels, loop (FR-021)

```csharp
for (var k = 0; k < items.Length; k++)
{
    switch (items[k])
    {
        case 0:
        case string { Length: 0 }:
            continue;                 // targets the for loop
        default:
            Log("other");
            break;
        case ImageClip i:
            if (i.Seconds == 0) break;   // targets the switch
            Log(i.Path);
            break;
    }
}
```

```ts
for (let k = 0; k < items.length; k++) {
  $switch: {
    const $s = items[k];

    if ($s === 0 || (typeof $s === "string" && $s.length === 0)) {
      continue;
    } else if ($s instanceof ImageClip) {
      const i = $s;
      if (i.seconds === 0) break $switch;
      log(i.path);
    } else {
      log("other");
    }
  }
}
```

- `default` moved to the final `else`; C# runs it only after every other label fails.
- The multi-label section tests the OR of its labels and binds nothing.
- The conditional `break` became `break $switch`, so it does not leave the `for` loop. `continue` still targets the loop.
- A switch with only constant labels keeps its native TS `switch` (NFR-001). A nested pattern switch is labeled `$switch1`. `goto case` / `goto default` in a pattern switch are MS0001.

## Relational pattern over a union (FR-033)

```csharp
public static string Size(Id id) => id switch { > 5 => "big", string s => s, int => "small" };
```

```ts
export function size(id: Id): string {
  return (($s) => {
    if (typeof $s === "number" && $s > 5) {
      return "big";
    }

    if (typeof $s === "string") {
      const s = $s;
      return s;
    }

    if (typeof $s === "number") {
      return "small";
    }

    throw new Error("Non-exhaustive switch expression");
  })(id);
}
```

`size("10")` returns `"10"`, as in C#. The relational arm is not an unguarded type pattern, so the `never` check is omitted (FR-020).

## Overload and constructor dispatch (FR-032)

```csharp
public static string F(Clip c) => "clip";
public static string F(string s) => "string";
```

```ts
export function f(...args: unknown[]): string {
  const [a0] = args;
  if (a0 instanceof ImageClip || a0 instanceof SilenceClip) {
    return fClip(a0);
  }

  if (typeof a0 === "string") {
    return fString(a0);
  }

  throw new Error("No matching overload for f");
}
```

The shape around the checks is today's dispatcher output (illustrative here). The union parameter is tested as the OR of its case tests, never as `instanceof Clip` (`Clip` has no runtime value). A constructor overloaded on `Id` tests `typeof a0 === "number" || typeof a0 === "string"` in the same way.

## Union nested in a class

```csharp
public class Outer { public union U(ImageClip, SilenceClip); }
```

```ts
import type { ImageClip } from "./image-clip";
import type { SilenceClip } from "./silence-clip";

export class Outer { /* … */ }

export namespace Outer {
  export type U = ImageClip | SilenceClip;
}
```

The case imports are collected on the companion-namespace path (spec 006).

## Union guard (FR-017, FR-035)

The guard is emitted by the IR-based union bridge; `TypeGuardBuilder` skips unions. It validates `unknown` input, so integer cases use the `isInt32` family:

```csharp
[GenerateGuard] public union Id(int, string);
[GenerateGuard] public union Clip(ImageClip, SilenceClip);
```

```ts
// id.ts
import { isInt32 } from "metano-runtime";

export type Id = number | string;

export function isId(value: unknown): value is Id {
  return isInt32(value) || typeof value === "string";
}
```

`isId(5.5)` is `false`, matching C#.

```ts
// clip.ts
import { ImageClip } from "./image-clip";
import { SilenceClip } from "./silence-clip";

export type Clip = ImageClip | SilenceClip;

export function isClip(value: unknown): value is Clip {
  return value instanceof ImageClip || value instanceof SilenceClip;
}
```

Because the file emits a guard, the case classes are **value** imports here (FR-017). Without `[GenerateGuard]`, they stay `import type`. The runtime import specifier follows today's runtime-helper import rules.

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

```dart
/// union: T | None
typedef Opt<T> = Object;
```

A switch expression over a union ends with `_ => throw StateError('Non-exhaustive switch expression')`, also when the scrutinee is nullable. A pattern switch statement renders as a native Dart 3 `switch` (no implicit fall-through; `break` leaves the switch), with `default:` moved last:

```dart
switch (c) {
  case ImageClip i:
    if (i.seconds == 0) break;
    log(i.path);
  case SilenceClip s when s.seconds > 10:
    log('long');
  default:
    log('other');
}
```

`Equals` / `GetHashCode` over a union render the core node of FR-010.
