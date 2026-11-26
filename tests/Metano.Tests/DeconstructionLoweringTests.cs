using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// #779: every deconstruction C# accepts in statement position lowers to
/// destructuring bindings and plain writes that keep C#'s evaluation order.
/// </summary>
public class DeconstructionLoweringTests
{
    private const string Source = """
        [Transpile, EmitInFile("deconstruction")]
        public record Point(int X, int Y);

        [Transpile, EmitInFile("deconstruction")]
        public class Pair
        {
            public void Deconstruct(out int first, out string second)
            {
                first = 7;
                second = "q";
            }
        }

        [Transpile, EmitInFile("deconstruction")]
        public class Holder
        {
            public int Field;
            public int Prop { get; set; }
            public int[] Items = new int[3];
            private int _a = 1;
            private int _b = 2;
            public void Swap() => (_a, _b) = (_b, _a);
        }

        [Transpile, EmitInFile("deconstruction")]
        public static class Shapes
        {
            public static (int, Action) Run(Func<Action, (int, Action)> body) => body(() => { });
            public static (int, string) Pair() => (1, "x");
            public static int[] Array(int tag) => new int[tag];
            public static int Next(int value) => value;

            public static int Literal() { var (n, s) = (1, "x"); return n + s.Length; }
            public static int Inferred() { var (n, dispose) = Run(d => (42, d)); dispose(); return n; }
            public static int Record() { var (x, y) = new Point(1, 2); return x + y; }
            public static int Method() { var (a, b) = new Pair(); return a + b.Length; }
            public static int Separate() { (var a, var b) = Pair(); return a + b.Length; }
            public static int Swap(int x, int y) { (x, y) = (y, x); return x - y; }
            public static int Nested() { var (a, (b, c)) = (1, (2, 3)); return a + b + c; }
            public static int NestedValue((int, (Point, Pair)) t) { var (a, ((x, y), (p, q))) = t; return a + x + y + p + q.Length; }
            public static int Discard() { var (_, b) = (Next(1), 2); return b; }
            public static int Mixed(int x) { (x, var z) = Pair(); return x + z.Length; }
            public static int Members(Holder h, int i) { (h.Field, h.Prop, Array(i)[Next(i)]) = (5, Next(6), 7); return h.Field + h.Prop; }
            public static int Dictionary(System.Collections.Generic.Dictionary<string, int> d) { var t = 0; foreach (var (k, v) in d) t += k.Length + v; return t; }
            public static int Records(Point[] points) { var t = 0; foreach (var (x, y) in points) t += x * y; return t; }
            public static int Lambda() { var x = 0; Action f = () => (x, _) = (9, 1); f(); return x; }
            public static int Alias(int i, int[] a) { (i, a[i]) = (1, 2); return i; }
            public static long Widen() { (long l, decimal d) = Pair2(); long m = 0; (m, _) = Pair2(); return l + m + (long)d; }
            public static (int, int) Pair2() => (1, 2);
            public static int DeconstructLast(Pair p) { int x = 0, y = 0, w = 0; (x, (y, _), w) = (Next(1), p, Next(3)); return x + y + w; }
        }
        """;

    private const string Expected = """
        import { HashCode, arrayLength, copyRecordWith, int64FromDecimal } from "metano-runtime";
        import { Dictionary } from "metano-runtime/system/collections";
        import { Decimal } from "metano-runtime/system/decimal";

        export class Point {
          declare readonly x: number;
          declare readonly y: number;

          constructor(x: number, y: number) {
            this.x = x;
            this.y = y;
          }

          equals(other: any): boolean {
            return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.x === other.x && this.y === other.y;
          }

          hashCode(): number {
            const hc = new HashCode();
            hc.add(this.x);
            hc.add(this.y);

            return hc.toHashCode();
          }

          with(overrides?: Partial<Point>): Point {
            return copyRecordWith<Point>(this, overrides);
          }

          protected $printMembers(): string {
            return `X = ${this.x}, Y = ${this.y}`;
          }

          toString(): string {
            return `Point { ${this.$printMembers()} }`;
          }
        }

        export class Pair {
          deconstruct(): readonly [first: number, second: string] {
            let first: number;
            let second: string;
            first = 7;
            second = "q";

            return [first, second];
          }
        }

        export class Holder {
          declare field: number;
          declare prop: number;
          declare items: number[];
          declare private _a: number;
          declare private _b: number;

          constructor() {
            this.field = 0;
            this.prop = 0;
            this.items = new Array<number>(3).fill(0);
            this._a = 1;
            this._b = 2;
          }

          swap(): void {
            const $part0 = this._b;
            const $part1 = this._a;
            this._a = $part0;
            this._b = $part1;
          }
        }

        export class Shapes {
          private constructor() { }

          static run(body: (arg: () => void) => [number, () => void]): [number, () => void] {
            return body(() => {
            });
          }

          static pair(): [number, string] {
            return [1, "x"] as [number, string];
          }

          static array(tag: number): number[] {
            return new Array<number>(arrayLength(tag)).fill(0);
          }

          static next(value: number): number {
            return value;
          }

          static literal(): number {
            const n = 1;
            const s = "x";

            return (n + s.length) | 0;
          }

          static inferred(): number {
            const [n, dispose] = Shapes.run((d: () => void) => [42, d] as [number, () => void]);
            dispose();

            return n;
          }

          static record(): number {
            const { x, y } = new Point(1, 2);

            return (x + y) | 0;
          }

          static method(): number {
            const [a, b] = new Pair().deconstruct();

            return (a + b.length) | 0;
          }

          static separate(): number {
            const [a, b] = Shapes.pair();

            return (a + b.length) | 0;
          }

          static swap(x: number, y: number): number {
            const $part0 = y;
            const $part1 = x;
            x = $part0;
            y = $part1;

            return (x - y) | 0;
          }

          static nested(): number {
            const a = 1;
            const b = 2;
            const c = 3;

            return (a + b + c) | 0;
          }

          static nestedValue(t: [number, [Point, Pair]]): number {
            const [a, [{ x, y }, $part0]] = t;
            const [p, q] = $part0.deconstruct();

            return (a + x + y + p + q.length) | 0;
          }

          static discard(): number {
            Shapes.next(1);
            const b = 2;

            return b;
          }

          static mixed(x: number): number {
            const [$part0, z] = Shapes.pair();
            x = $part0;

            return (x + z.length) | 0;
          }

          static members(h: Holder, i: number): number {
            const $part0 = h;
            const $part1 = h;
            const $part2 = Shapes.array(i);
            const $part3 = Shapes.next(i);
            const $part4 = Shapes.next(6);
            $part0.field = 5;
            $part1.prop = $part4;
            $part2[$part3] = 7;

            return (h.field + h.prop) | 0;
          }

          static dictionary(d: Dictionary<string, number>): number {
            let t = 0;
            for (const $item0 of d) {
              const [k, v] = $item0;
              t = (t + (k.length + v)) | 0;
            }

            return t;
          }

          static records(points: Point[]): number {
            let t = 0;
            for (const $item0 of points) {
              const { x, y } = $item0;
              t = (t + Math.imul(x, y)) | 0;
            }

            return t;
          }

          static lambda(): number {
            let x = 0;
            const f = () => {
              x = 9;
            };
            f();

            return x;
          }

          static alias(i: number, a: number[]): number {
            const $part0 = a;
            const $part1 = i;
            i = 1;
            $part0[$part1] = 2;

            return i;
          }

          static widen(): bigint {
            const [$part0, $part1] = Shapes.pair2();
            const l = BigInt($part0);
            const d = new Decimal($part1 + 0);
            let m = 0n;
            const [$part2] = Shapes.pair2();
            m = BigInt($part2);

            return BigInt.asIntN(64, BigInt.asIntN(64, l + m) + int64FromDecimal(d));
          }

          static pair2(): [number, number] {
            return [1, 2] as [number, number];
          }

          static deconstructLast(p: Pair): number {
            let x = 0;
            let y = 0;
            let w = 0;
            const $part0 = Shapes.next(1);
            const $part1 = p;
            const $part2 = Shapes.next(3);
            const [$part3] = $part1.deconstruct();
            x = $part0;
            y = $part3;
            w = $part2;

            return (x + y + w) | 0;
          }
        }

        """;

    [Test]
    public async Task Deconstructions_LowerToDestructuringAndOrderedWrites()
    {
        var result = TranspileHelper.Transpile(Source);

        await Assert.That(result["deconstruction.ts"]).IsEqualTo(Expected);
    }

    [Test]
    public async Task Deconstructions_TypeCheckStrictly()
    {
        var result = TranspileHelper.Transpile(Source);

        var (exitCode, output) = await TreeShaking.StrictTypeCheck.Run(
            result,
            new Dictionary<string, string>()
        );

        await Assert.That(output).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task Deconstructions_ReportNothing()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Source);

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();
    }

    [Test]
    public async Task UnreadableShapes_ReportUnsupported()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, EmitInFile("unreadable")]
            public class Box
            {
                public int Value;
            }

            [Transpile, EmitInFile("unreadable")]
            public static class BoxExtensions
            {
                public static void Deconstruct(this Box box, out int value, out int twice)
                {
                    value = box.Value;
                    twice = box.Value * 2;
                }
            }

            [Transpile, EmitInFile("unreadable")]
            public static class Unreadable
            {
                public static int Extension(Box box) { var (a, b) = box; return a + b; }
                public static int Date(System.DateOnly date) { var (y, m, d) = date; return y + m + d; }
                public static int Indexer(System.Collections.Generic.List<int> list) { (list[0], var z) = (1, 2); return z; }
            }
            """
        );

        var messages = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .Select(d => d.Message)
            .ToList();

        await Assert.That(messages.Count).IsEqualTo(3);
        await Assert.That(messages[0]).Contains("'BoxExtensions.Deconstruct'");
        await Assert.That(messages[1]).Contains("'DateOnly.Deconstruct'");
        await Assert.That(messages[2]).Contains("'list[0]'");

        await Assert
            .That(files["unreadable.ts"])
            .Contains("Unsupported expression: deconstruction");
    }

    /// <summary>
    /// A deconstruction whose value is used is no statement: it stays reported.
    /// </summary>
    [Test]
    public async Task DeconstructionAsValue_ReportsUnsupported()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public static class Swaps
            {
                public static int Swap(int x, int y)
                {
                    var swapped = (x, y) = (y, x);
                    return swapped.Item1;
                }
            }
            """
        );

        await Assert
            .That(diagnostics.Count(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEqualTo(1);
    }

    /// <summary>
    /// Dart prints a tuple deconstruction as a record pattern and a tuple
    /// literal as one declaration per element.
    /// </summary>
    [Test]
    public async Task Dart_TupleDeconstruction_PrintsARecordPattern()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public static class Tuples
            {
                public static (int, (string, bool)) Make() => (1, ("x", true));
                public static int Read() { var (a, (b, _)) = Make(); var (c, d) = (2, 3); return a + b.Length + c + d; }
            }
            """
        );

        var output = files["tuples.dart"];

        await Assert.That(output).Contains("    var (a, (b, _)) = Tuples.make();");
        await Assert.That(output).Contains("    var c = 2;\n    var d = 3;");
    }
}
