# Quickstart: Overload factories, selector symbols and direct call sites

How to see the feature work once its slices land. Commands follow CLAUDE.md (`dotnet run` for
TUnit, Bun for JavaScript, Node for the load-order checks).

## 1. A dispatched class

```csharp
using Metano.Annotations;

[assembly: TranspileAssembly]

namespace Demo;

public class Dimension
{
    public int Size;
    public int Width;
    public int Height;

    public Dimension(int size) => Size = size;
    public Dimension(int width, int height)
    {
        Width = width;
        Height = height;
    }
}
```

```ts
export class Dimension {
  protected static readonly bySize: unique symbol = Symbol("Dimension(int size)");
  protected static readonly byWidthHeight: unique symbol = Symbol("Dimension(int width, int height)");

  constructor(overload: typeof Dimension.bySize, size: number);
  constructor(overload: typeof Dimension.byWidthHeight, width: number, height: number);
  constructor(size: number);
  constructor(width: number, height: number);
  constructor(...args: unknown[]) { /* selector branches first, then today's fallback */ }

  static fromSize(size: number): Dimension {
    return new Dimension(Dimension.bySize, size);
  }

  static fromWidthHeight(width: number, height: number): Dimension {
    return new Dimension(Dimension.byWidthHeight, width, height);
  }
}
```

```ts
Dimension.fromSize(3);                    // the factory — what C# new Dimension(3) emits
new Dimension(3);                         // hand-written TS still works (the fallback)
new Dimension(Dimension.bySize, 3);       // TS2445: protected — outsiders use the factories
```

## 2. A derived class picks the base overload

```csharp
public class Box3D : Dimension
{
    public int Depth;

    public Box3D(int width, int height, int depth) : base(width, height) => Depth = depth;
}
```

```ts
export class Box3D extends Dimension {
  depth: number;

  constructor(width: number, height: number, depth: number) {
    super(Dimension.byWidthHeight, width, height);   // the exact base overload
    this.depth = depth;
  }
}
```

## 3. Direct call sites

| C# | TypeScript |
|---|---|
| `new Dimension(3, 4)` | `Dimension.fromWidthHeight(3, 4)` |
| `shapes.Add(1, 2)` (overloaded method) | `shapes.addXY(1, 2)` — no dispatcher on the hot path |
| `new Money(19.9m)` | `Money.fromAmount(19.9)` |

## 4. Run the checks

```sh
dotnet run --project tests/Metano.Tests/                                  # goldens + MS0067 tests
cd targets/js/sample-issue-tracker && bun run build && bun test           # factories + direct call sites (tsgo --strict build)
```

## 5. What to expect in a 2.x consumer

- Hand-written `new T(...)` calls keep compiling and running (the fallback).
- `(obj as any).addXY(...)`-style fast-path hacks may hit renamed members (`fInt` → `fInt32`,
  `now` → `nowDefault`, collisions now climb namespaces) — derive the new name from
  contracts/naming.md or pin it with `[Name]`.
- A consumer rebuilt with 3.0 against a package still built with 2.x fails at `tsc` on the missing
  factory — rebuild the producer first.
- `MS0067` warns wherever a generated name had to take a `$` suffix.
