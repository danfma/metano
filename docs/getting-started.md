# Getting Started

This guide walks you through creating your first Metano project from scratch —
annotating a C# class, running the transpiler, and consuming the generated
TypeScript from a Bun project.

## Prerequisites

- **.NET SDK 10.0** (preview) — [download](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Bun 1.3+** — [install](https://bun.sh)
- **Git**

## The mental model

Before we touch any code, it helps to understand how Metano thinks about your
project layout:

- **C# project** (your `.csproj`) — the **source** of types and logic.
- **TypeScript package** (a directory with its own `package.json`) — the
  **target** where generated `.ts` files land.

When you run `dotnet build`, Metano takes every type marked for transpilation
in the C# project and writes the resulting TypeScript files into the target
package's `src/` directory. The target is a **real npm package** that lives on
its own — it has its own `package.json`, its own `tsconfig.json`, its own
bundler config (Vite, Bun, esbuild, whatever you prefer), and its own tests.

The target package can live wherever makes sense for your workflow:

- **Alongside the C# project** in the same repository (e.g., `MyDomain/` next
  to `MyDomain-ts/`)
- **In a different folder** entirely (monorepo sibling, or a separate repo
  used as a workspace dependency)
- **Even outside the repository** if you want to ship it as an npm package
  that other projects install

**Why this split?** Because it keeps you in both ecosystems without
compromises. On the C# side, you use your normal IDE, nuget packages, tests,
and debugging. On the TypeScript side, you keep **everything you love about
modern JS tooling** — Vite dev server with hot module replacement, Bun test
watch mode, source maps, bundler tree-shaking, your preferred linter and
formatter. The generated `.ts` files look and behave like files you would
have written by hand, so all your JS tools work on them without any special
integration. You get the *best of both worlds*: your domain logic stays in
C# (one source of truth), and your frontend stays buttery-smooth with its
native dev loop.

The rest of this guide creates both sides step by step so you can see how it
fits together.

## Step 1: Create the C# source project

Pick a folder where both the C# side and the TS side will live. For this
guide we'll use a single parent directory with two siblings:

```
my-app/
├── my-domain/        ← C# project (source)
└── my-domain-ts/     ← TypeScript package (target)
```

Create the C# side first:

```bash
mkdir -p my-app && cd my-app
mkdir my-domain && cd my-domain
dotnet new classlib
```

Edit the generated `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>preview</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Metano" Version="0.1.*" />
    <PackageReference Include="Metano.Build" Version="0.1.*" />
  </ItemGroup>

  <PropertyGroup>
    <!-- Where the generated .ts files land. This path is relative to the
         .csproj and should point at the `src/` directory of your target
         TypeScript package. -->
    <MetanoOutputDir>../my-domain-ts/src</MetanoOutputDir>
    <!-- Wipe the output directory before each build so stale files from
         renamed/deleted C# types don't linger. Safe because the folder is
         entirely generator-owned. -->
    <MetanoClean>true</MetanoClean>
  </PropertyGroup>
</Project>
```

**Two NuGet packages:**

- **`Metano`** — the attributes (`[Transpile]`, `[StringEnum]`, etc.) and BCL
  runtime mappings. This is the package your C# code actually references.
- **`Metano.Build`** — a tiny MSBuild integration that hooks into
  `dotnet build` and runs the transpiler as a post-build step. No code of
  its own — just a `.targets` file that invokes the `metano-typescript` CLI
  automatically.

**Key property: `MetanoOutputDir`**

This tells Metano where to write the generated TypeScript. It's a path
**relative to the `.csproj` file** and should point at the `src/` folder of
whatever TypeScript package will consume the output. In this guide we're
using `../my-domain-ts/src` — a sibling directory we'll create next.

## Step 2: Create the target TypeScript package

The target is a normal npm package. You can use Bun, npm, pnpm, or yarn —
whatever fits your workflow. For this guide we'll use Bun:

```bash
cd ..                           # back to my-app/
mkdir my-domain-ts && cd my-domain-ts
bun init -y
mkdir src
```

You now have `my-domain-ts/package.json`, `my-domain-ts/tsconfig.json`, and
an empty `my-domain-ts/src/` folder where Metano will write the generated
files.

Add `metano-runtime` as a dependency (most generated code imports from it):

```bash
bun add metano-runtime
```

That's it — this package is **your normal TypeScript project**. You can add
Vite, Next.js, Remix, React, Svelte, Playwright, Jest, Biome, ESLint,
Prettier, whatever you want. None of it has to know that `src/` is
generated — to your JS tooling it's just a regular TypeScript project.

### Why this structure

The key insight is that the target package is a **plain TypeScript project**
that happens to have its `src/` populated by a code generator. This matters
because it means:

- **Your frontend dev loop is unchanged.** If you're using Vite, run
  `vite dev` in `my-domain-ts/` and you get HMR just like you would with any
  other project. Metano regenerates `.ts` files on `dotnet build`, and Vite
  picks up the changes via its normal file watcher.
- **Your bundler doesn't need a plugin.** Tree-shaking, code splitting,
  source maps — all of it works because the generated code is idiomatic
  TypeScript.
- **Your tests stay in JS.** Use `bun test`, Jest, Vitest — whatever you
  like. Tests can import from `./src` like any other module.
- **Your linter/formatter works.** Biome, ESLint, Prettier — point them at
  `src/` and they'll happily format the generated code (Metano's output is
  already close to idiomatic, so this is usually a no-op).

In short: **C# gives you one source of truth for domain logic; TypeScript
tooling stays in charge of everything else.**

## Step 3: Write some C#

Delete the default `Class1.cs` and create `Product.cs`:

```csharp
using Metano.Annotations;

[assembly: TranspileAssembly]
[assembly: EmitPackage("my-domain")]

namespace MyDomain;

[StringEnum]
public enum Category
{
    Books,
    Electronics,
    Clothing,
}

public record Product(string Name, decimal Price, Category Category)
{
    public Product ApplyDiscount(decimal percent) =>
        this with { Price = Price * (1 - percent / 100) };

    public bool IsExpensive => Price > 100;
}
```

What these attributes do:

- **`[assembly: TranspileAssembly]`** — transpile every public type in this
  assembly. Without it, you'd have to mark each type with `[Transpile]` individually.
- **`[assembly: EmitPackage("my-domain")]`** — sets the npm package name for the
  generated TypeScript output. If another C# project references this one, its
  imports will resolve to `import { Product } from "my-domain"`.
- **`[StringEnum]`** — emits `Category` as a string union (`"Books" | "Electronics" | "Clothing"`)
  instead of a numeric enum.

## Step 4: Build

From the C# project directory:

```bash
cd ../my-domain     # back to the csproj side if you're in my-domain-ts
dotnet build
```

You should see output like:

```
Metano: transpiling MyDomain...
  Generated: category.ts
  Generated: product.ts
  Generated: index.ts
  Updated: ../my-domain-ts/package.json
Metano: 3 file(s) generated in ../my-domain-ts/src
```

Metano wrote the files into `my-domain-ts/src/` (the `MetanoOutputDir` you
configured) **and** merged a `metano-runtime` + `decimal.js` dependency into
`my-domain-ts/package.json`. Every subsequent `dotnet build` regenerates the
files; the `MetanoClean` flag wipes the output directory first so renamed or
deleted C# types don't leave stale `.ts` files behind.

## Step 5: Inspect the output

`../my-domain-ts/src/category.ts`:

```typescript
export const Category = {
  Books: "Books",
  Electronics: "Electronics",
  Clothing: "Clothing",
} as const;

export type Category = (typeof Category)[keyof typeof Category];
```

`../my-domain-ts/src/product.ts`:

```typescript
import { HashCode } from "metano-runtime";
import { Decimal } from "decimal.js";
import type { Category } from "./category";

export class Product {
  constructor(
    readonly name: string,
    readonly price: Decimal,
    readonly category: Category,
  ) {}

  applyDiscount(percent: Decimal): Product {
    return this.with({
      price: this.price.times(new Decimal(1).minus(percent.dividedBy(100))),
    });
  }

  get isExpensive(): boolean {
    return this.price.greaterThan(100);
  }

  equals(other: any): boolean {
    return (
      other instanceof Product &&
      this.name === other.name &&
      this.price.equals(other.price) &&
      this.category === other.category
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.name);
    hc.add(this.price);
    hc.add(this.category);
    return hc.toHashCode();
  }

  with(overrides?: Partial<Product>): Product {
    return new Product(
      overrides?.name ?? this.name,
      overrides?.price ?? this.price,
      overrides?.category ?? this.category,
    );
  }
}
```

The transpiler also generated `package.json` with:

```json
{
  "name": "my-domain",
  "type": "module",
  "dependencies": {
    "metano-runtime": "^0.1.0",
    "decimal.js": "^10.6.0"
  }
}
```

## Step 6: Consume from your TypeScript package

The `my-domain-ts` package you created in Step 2 now has generated files in
`src/`. Make sure the new `decimal.js` dependency (added automatically by the
transpiler to `package.json`) is installed:

```bash
cd ../my-domain-ts
bun install
```

Create a quick smoke-test script at `my-domain-ts/test.ts`:

```typescript
import { Product, Category } from "./src";
import { Decimal } from "decimal.js";

const book = new Product("Clean Code", new Decimal(45), "Books");
console.log(book.isExpensive);       // false
console.log(book.applyDiscount(new Decimal(10)).price.toString()); // "40.5"
```

Run it:

```bash
bun run test.ts
```

From here on, this is **just a TypeScript package**. Add your frontend
framework of choice, point Vite at it, write tests, import it from other
packages — the generated code has no Metano-specific requirements beyond
`metano-runtime`.

### The edit / run loop

During development the flow is:

1. Edit C# in `my-domain/`
2. `dotnet build` (or let your IDE do it on save) — regenerates
   `my-domain-ts/src/`
3. Your JS dev tooling picks up the file changes automatically

If you're running `vite dev` or `bun --watch` in `my-domain-ts/`, you'll see
the browser reload / test runner re-run as soon as the generator finishes.
No extra plugin, no custom integration — the generated `.ts` files look like
any other files on disk as far as the bundler and test runner are concerned.

## Where to go next

- **[Attribute Reference](attributes.md)** — Learn every attribute Metano supports
- **[BCL Type Mappings](bcl-mappings.md)** — See what C# types become in TypeScript
- **[Cross-Project References](cross-package.md)** — Share types between multiple C# projects
- **[JSON Serialization](serialization.md)** — Transpile `JsonSerializerContext` for JSON round-trips
- **[Sample projects](../samples/)** — See realistic examples:
  - [SampleTodo](../samples/SampleTodo/README.md) — basic records + enums
  - [SampleTodo.Service](../samples/SampleTodo.Service/README.md) — Hono CRUD
  - [SampleIssueTracker](../samples/SampleIssueTracker/README.md) — complex domain
