namespace Metano.Tests;

/// <summary>
/// The value binding of a closed type lowered to a union types its
/// <c>Symbol.hasInstance</c> so <c>v instanceof X</c> narrows to the union, while
/// <c>instanceof</c> a case, or any class below one, keeps narrowing to that class
/// (#758, decision W9-2). Checked through declaration emit: a consumer that sees only the
/// emitted <c>.d.ts</c> files narrows the same way.
/// </summary>
public class ClosedFamilyInstanceofTests
{
    private const string Library = """
        [assembly: TranspileAssembly]

        namespace Acme
        {
            public closed class Shape
            {
                public string Label() => "shape";
            }

            public sealed class Circle : Shape
            {
                public string Radius() => "r";
            }

            public closed class Poly : Shape
            {
                public string Corners() => "c";
            }

            public sealed class Tri : Poly;

            public sealed class Quad : Poly;

            public closed class Tree<T>;

            public sealed class Leaf<T>(T value) : Tree<T>
            {
                public T Value { get; } = value;
            }

            public sealed class Fork<T> : Tree<T>;
        }
        """;

    /// <summary>
    /// Each assignment of a narrowed value to an <c>Exactly</c> check compiles only when
    /// the narrowed type is the one named.
    /// </summary>
    private const string Consumer = """
        import { Shape } from "../dts/acme/shape.js";
        import { Circle } from "../dts/acme/circle.js";
        import { Poly } from "../dts/acme/poly.js";
        import { Tree } from "../dts/acme/tree.js";
        import { Leaf } from "../dts/acme/leaf.js";

        type Exactly<A, B> =
          (<T>() => T extends A ? 1 : 2) extends (<T>() => T extends B ? 1 : 2) ? true : false;

        export class BigCircle extends Circle {
          big(): string {
            return "big";
          }
        }

        export function read(value: unknown): string {
          if (value instanceof Shape) {
            const exact: Exactly<typeof value, Shape> = true;

            switch (value.kind) {
              case "Circle":
                return value.radius();
              case "Tri":
              case "Quad":
                return value.corners();
              default:
                return value satisfies never;
            }
          }

          if (value instanceof Poly) {
            const exact: Exactly<typeof value, Poly> = true;
          }

          if (value instanceof Circle) {
            const exact: Exactly<typeof value, Circle> = true;
          }

          return "";
        }

        export function sub(shape: Shape): string {
          if (shape instanceof BigCircle) {
            const exact: Exactly<typeof shape, BigCircle> = true;

            return shape.big();
          }

          if (shape instanceof Poly) {
            const exact: Exactly<typeof shape, Poly> = true;
          }

          return shape.label();
        }

        export function leaf(tree: Tree<number>): number {
          if (tree instanceof Leaf) {
            const exact: Exactly<typeof tree, Leaf<number>> = true;

            return tree.value;
          }

          return tree instanceof Tree ? 0 : -1;
        }
        """;

    [Test]
    public async Task InstanceofNarrowsToTheUnion_ThroughDeclarationEmit()
    {
        var library = TranspileHelper.Transpile(Library + ClosedFamilyUnionTests.ClosedPolyfill);

        await Assert.That(library["acme/shape.ts"]).Contains("[Symbol.hasInstance]");

        var root = Directory.CreateTempSubdirectory("metano-hasinstance-").FullName;

        try
        {
            var emit = Path.Combine(root, "emit");

            TreeShaking.StrictTypeCheck.WriteFiles(Path.Combine(emit, "lib"), library);
            File.WriteAllText(Path.Combine(emit, "tsconfig.json"), EmitConfig);

            var (emitExit, emitOutput) = await TreeShaking.StrictTypeCheck.RunTsgo(emit);

            await Assert.That(emitOutput).IsEmpty();
            await Assert.That(emitExit).IsEqualTo(0);

            var consumer = Path.Combine(root, "consumer");

            TreeShaking.StrictTypeCheck.WriteFiles(
                Path.Combine(consumer, "app"),
                new Dictionary<string, string> { ["read.ts"] = Consumer }
            );

            File.WriteAllText(Path.Combine(consumer, "tsconfig.json"), ConsumerConfig);

            var (consumerExit, consumerOutput) = await TreeShaking.StrictTypeCheck.RunTsgo(
                consumer
            );

            await Assert.That(consumerOutput).IsEmpty();
            await Assert.That(consumerExit).IsEqualTo(0);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private const string EmitConfig = """
        {
          "compilerOptions": {
            "strict": true,
            "noImplicitOverride": true,
            "declaration": true,
            "composite": true,
            "emitDeclarationOnly": true,
            "rootDir": "./lib",
            "outDir": "../consumer/dts",
            "target": "es2022",
            "lib": ["ESNext"],
            "module": "esnext",
            "moduleResolution": "bundler",
            "types": []
          },
          "include": ["lib/**/*.ts"]
        }
        """;

    private const string ConsumerConfig = """
        {
          "compilerOptions": {
            "strict": true,
            "noImplicitOverride": true,
            "noUnusedLocals": false,
            "noEmit": true,
            "target": "es2022",
            "lib": ["ESNext"],
            "module": "esnext",
            "moduleResolution": "bundler",
            "types": []
          },
          "include": ["app/**/*.ts", "dts/**/*.d.ts"]
        }
        """;
}
