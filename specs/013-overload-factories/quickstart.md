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

    private Dimension(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public static Dimension Square(int side) => new Dimension(side, side);
}
```

```ts
export class Dimension {
  protected static readonly byDimensionSize: unique symbol = Symbol("Dimension(int size)");
  protected static readonly byDimensionWidthHeight: unique symbol = Symbol("Dimension(int width, int height)");

  constructor(overload: typeof Dimension.byDimensionSize, size: number);
  constructor(overload: typeof Dimension.byDimensionWidthHeight, width: number, height: number);
  constructor(width: number, height: number);
  constructor(size: number);
  constructor(...args: unknown[]) { /* guarded selector branches first, then today's fallback */ }

  static fromSize(size: number): Dimension {
    return new Dimension(Dimension.byDimensionSize, size);
  }

  private static fromWidthHeight(width: number, height: number): Dimension {
    return new Dimension(Dimension.byDimensionWidthHeight, width, height);
  }

  static square(side: number): Dimension {
    return Dimension.fromWidthHeight(side, side);
  }
}
```

```ts
Dimension.fromSize(3);                              // the public factory
Dimension.fromWidthHeight(3, 4);                    // TS2341: private, as in C#
new Dimension(3);                                   // hand-written TS still works (the fallback)
new Dimension(Dimension.byDimensionSize, 3);        // TS2445: protected — outsiders use the factories
```

The constructor keeps the most permissive accessibility (FR-049, `MS0060` for the private one);
each factory keeps its own overload's.

## 2. A derived class picks the base overload

```csharp
public class Box3D : Dimension
{
    public int Depth;

    public Box3D(int size, int depth) : base(size) => Depth = depth;
}
```

```ts
export class Box3D extends Dimension {
  depth: number = 0;

  constructor(size: number, depth: number) {
    super(Dimension.byDimensionSize, size);   // the exact base overload
    this.depth = depth;
  }
}
```

## 3. Virtual overloads

```csharp
public class Base
{
    public virtual int Add(int x) => x;
    public virtual int Add(string s) => s.Length;
}

public class Derived : Base
{
    public override int Add(int x) => x * 2;
}
```

`Derived` emits `override addX(x: number): number` and no dispatcher; `new Derived().add("abc")`
returns `3`, as in C# (today: `NaN`, research R8).

## 4. Direct call sites

| C# | TypeScript |
|---|---|
| `new Dimension(3)` | `Dimension.fromSize(3)` |
| `shapes.Add(1, 2)` (overloaded method) | `shapes.addXY(1, 2)` — no dispatcher on the hot path |
| `b.Add(1)` on a `Base` | `b.addX(1)` — virtual |
| `new Money(19.9m)` | `Money.fromAmount(new Decimal("19.9"))` |

## 5. Run the checks

```sh
dotnet run --project tests/Metano.Tests/                                  # goldens, naming units, MS0067 / MS0068
cd targets/js/sample-issue-tracker && bun run build && bun test           # factories + direct call sites
node targets/js/sample-issue-tracker/dist/<entry>.js                      # the Node load-order run (FR-029)
```

## 6. What to expect in a 2.x consumer

- Hand-written `new T(...)` and `obj.method(...)` dispatcher calls keep compiling and running.
- A hand-written class implementing a generated interface must add the fast paths (TS2420).
- A hand-written subclass that overrides a generated dispatcher is bypassed by generated direct
  calls; override the fast path instead.
- `(obj as any).fastPath(...)` hacks may hit renamed members (`fInt` → `fInt32`, `now` →
  `nowDefault`, `sumIntArray` → `sumInt32Array`, `identityValue` → `identityT`).
- `MS0067` warns wherever a generated name took a `$`.
