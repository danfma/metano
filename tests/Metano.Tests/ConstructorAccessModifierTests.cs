using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;
using Microsoft.CodeAnalysis;

namespace Metano.Tests;

/// <summary>
/// FR-049 (#432): the TypeScript constructor carries the C# constructor's
/// accessibility, mixed overloads take the most permissive one (MS0060), a
/// type constructed outside its class body keeps a public constructor
/// (MS0061), and a private constructor of an extended class becomes
/// protected (TS2675).
/// </summary>
public class ConstructorAccessModifierTests
{
    private readonly List<string> _tempDirs = [];

    [After(Test)]
    public void DeleteTempDirs()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
            Directory.Delete(dir, recursive: true);
    }

    [Test]
    [Arguments("private", "  private constructor(x: number) { }")]
    [Arguments("private protected", "  private constructor(x: number) { }")]
    [Arguments("protected", "  protected constructor(x: number) { }")]
    [Arguments("public", "  constructor(x: number) { }")]
    [Arguments("internal", "  constructor(x: number) { }")]
    [Arguments("protected internal", "  constructor(x: number) { }")]
    public async Task Accessibility_MapsToTheConstructorModifier(string accessibility, string line)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            [Transpile]
            public class Widget
            {
                {{accessibility}} Widget(int x) { }
            }
            """
        );

        await Assert.That(files["widget.ts"]).Contains(line + "\n");
        await Assert.That(ConstructorAccessDiagnostics(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task RecordWithPrivateConstructor_EmitsPrivateConstructor()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Money
            {
                public int Cents { get; }

                private Money(int cents) { Cents = cents; }

                public static Money From(int cents) => new Money(cents);
            }
            """
        );

        var output = files["money.ts"];

        await Assert.That(output).Contains("  private constructor(readonly cents: number) {");
        await Assert.That(output).Contains("return new Money(cents);");
    }

    [Test]
    public async Task PositionalRecordsAndPrimaryConstructors_EmitNoModifier()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public record Point(int X);

            [Transpile]
            public abstract record Shape(string Name);

            [Transpile]
            public class Counter(int start) { public int Start => start; }

            [Transpile]
            public abstract class Gauge(int max) { public int Max => max; }
            """
        );

        await Assert.That(files["point.ts"]).Contains("  constructor(readonly x: number) { }");
        await Assert.That(files["shape.ts"]).Contains("  constructor(readonly name: string) { }");
        await Assert
            .That(files["counter.ts"])
            .Contains("  constructor(readonly start: number) { }");
        await Assert.That(files["gauge.ts"]).Contains("  constructor(readonly max: number) { }");
        await Assert.That(ConstructorAccessDiagnostics(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task ImplicitConstructorOfAbstractClass_EmitsNoModifier()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public abstract class Entity { public int Id { get; set; } }
            """
        );

        await Assert.That(files["entity.ts"]).Contains("  constructor() { }");
    }

    [Test]
    public async Task MixedAccessibility_EmitsTheMostPermissiveModifierAndReportsMs0060()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Range
            {
                public int Start { get; }

                protected Range(int start) { Start = start; }

                private Range() : this(0) { }
            }
            """
        );

        var warning = await SingleDiagnostic(
            diagnostics,
            DiagnosticCodes.MixedConstructorAccessibility
        );

        await Assert
            .That(files["range.ts"])
            .Contains("  protected constructor(readonly start: number) {");
        await Assert.That(warning.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(warning.Message).Contains("emitted protected");
        await Assert.That(warning.Message).Contains("'Range()' (private)");
        await Assert.That(warning.Location!.SourceSpan.Length).IsEqualTo("Range".Length);
    }

    [Test]
    public async Task MixedAccessibilityInADispatcher_SharesThePublicModifierAndReportsMs0060()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Size
            {
                public int Value { get; }

                public Size(int value) { Value = value; }

                public Size(string text) { Value = text.Length; }

                private Size(int a, int b) { Value = a + b; }
            }
            """
        );

        var output = files["size.ts"];
        var warning = await SingleDiagnostic(
            diagnostics,
            DiagnosticCodes.MixedConstructorAccessibility
        );

        await Assert.That(output).Contains("  constructor(a: number, b: number);\n");
        await Assert.That(output).Contains("  constructor(...args: unknown[]) {");
        await Assert.That(output).DoesNotContain("private constructor");
        await Assert.That(warning.Message).Contains("emitted public");
        await Assert.That(warning.Message).Contains("'Size(int, int)' (private)");
    }

    [Test]
    public async Task JsonContextTypeSpec_KeepsThePrivateConstructorPublicAndReportsMs0061()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            JsonContext(
                """
                [Transpile]
                public record Tag
                {
                    public string Name { get; }

                    private Tag(string name) { Name = name; }

                    public static Tag Of(string name) => new Tag(name);
                }
                """,
                "Tag"
            )
        );

        var warning = await SingleDiagnostic(
            diagnostics,
            DiagnosticCodes.ConstructorAccessibilityWidened
        );

        await Assert.That(files["app/tag.ts"]).Contains("  constructor(readonly name: string) {");
        await Assert.That(files["app/app-json-context.ts"]).Contains("new Tag(p.name as string)");
        await Assert.That(warning.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(warning.Message).Contains("instead of private");
        await Assert.That(warning.Message).Contains("JSON SerializerContext 'AppJsonContext'");
    }

    [Test]
    public async Task JsonContextTypeSpec_KeepsTheProtectedConstructorOfAnAbstractClassPublic()
    {
        // `type: Base` is typed `abstract new (...) => Base`, which a protected
        // constructor does not satisfy (TS2322), even though the factory throws.
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            JsonContext(
                """
                [Transpile]
                public abstract class Base
                {
                    protected Base() { }

                    public int Id { get; set; }
                }
                """,
                "Base"
            )
        );

        var warning = await SingleDiagnostic(
            diagnostics,
            DiagnosticCodes.ConstructorAccessibilityWidened
        );

        await Assert.That(files["app/base.ts"]).Contains("  constructor() { }");
        await Assert.That(files["app/app-json-context.ts"]).Contains("type: Base,");
        await Assert.That(warning.Message).Contains("instead of protected");
    }

    [Test]
    public async Task JsonContextPlainObject_ConstructsNoClass()
    {
        // A [PlainObject] emits an interface and an object-literal factory: no
        // class constructor exists, so nothing is widened.
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            JsonContext(
                """
                [Transpile, PlainObject]
                public record Payload(string Name);
                """,
                "Payload"
            )
        );

        await Assert.That(files["app/app-json-context.ts"]).DoesNotContain("new Payload");
        await Assert.That(ConstructorAccessDiagnostics(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task NewFromANestedType_KeepsThePrivateConstructorPublicAndReportsMs0061()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Report
            {
                private Report(string title) { Title = title; }

                public string Title { get; }

                [Transpile]
                public static class Builder
                {
                    public static Report Build(string title) => new Report(title);
                }
            }
            """
        );

        var output = files["report.ts"];
        var warning = await SingleDiagnostic(
            diagnostics,
            DiagnosticCodes.ConstructorAccessibilityWidened
        );

        await Assert.That(output).Contains("  constructor(readonly title: string) {");
        await Assert.That(output).Contains("return new Report(title);");
        await Assert.That(warning.Message).Contains("the nested type 'Report.Builder'");
    }

    [Test]
    public async Task NewFromANestedType_KeepsTheProtectedConstructorPublic()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Session
            {
                protected Session() { }

                [Transpile]
                public class Factory
                {
                    public Session Open() => new Session();
                }
            }
            """
        );

        await Assert
            .That(files["session.ts"])
            .Contains("export class Session {\n  constructor() { }");
        await SingleDiagnostic(diagnostics, DiagnosticCodes.ConstructorAccessibilityWidened);
    }

    [Test]
    public async Task NewFromADeeplyNestedType_NamesTheInnermostType()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Catalog
            {
                private Catalog() { }

                [Transpile]
                public class Section
                {
                    [Transpile]
                    public class Loader
                    {
                        public Catalog Load() => new Catalog();
                    }
                }
            }
            """
        );

        var warning = await SingleDiagnostic(
            diagnostics,
            DiagnosticCodes.ConstructorAccessibilityWidened
        );

        await Assert
            .That(files["catalog.ts"])
            .Contains("export class Catalog {\n  constructor() { }");
        await Assert.That(warning.Message).Contains("the nested type 'Catalog.Section.Loader'");
    }

    [Test]
    public async Task NestedTypeOutsideTheClassBody_UsingItsOwnConstructor_KeepsTheModifier()
    {
        // A nested type that constructs nothing of its container leaves the
        // container's private constructor alone.
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Ledger
            {
                private Ledger() { }

                public static Ledger Empty() => new Ledger();

                [Transpile]
                public class Entry
                {
                    public Entry Copy() => new Entry();
                }
            }
            """
        );

        await Assert.That(files["ledger.ts"]).Contains("  private constructor() { }");
        await Assert.That(ConstructorAccessDiagnostics(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task NestedVariantsOfAPrivateConstructor_LowerItToProtected()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public abstract record Result
            {
                private Result() { }

                [Transpile]
                public sealed record Ok(int Value) : Result;

                [Transpile]
                public sealed record Failed(string Error) : Result;
            }
            """
        );

        var output = files["result.ts"];

        await Assert
            .That(output)
            .Contains("export abstract class Result {\n  protected constructor() { }");
        await Assert.That(output).Contains("export class Ok extends Result {");
        await Assert.That(ConstructorAccessDiagnostics(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task ModuleLocalNestedSubclass_LowersTheOwnersPrivateConstructorToProtected()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Shape
            {
                private Shape() { }

                public static Shape Unit() => new Square();

                [Transpile]
                private sealed class Square : Shape { }
            }
            """
        );

        var output = files["shape.ts"];

        await Assert.That(output).Contains("  protected constructor() { }");
        await Assert.That(output).Contains("class Shape$Square extends Shape {");
    }

    [Test]
    public async Task NestedSubclassThatAlsoConstructsTheOwner_KeepsThePrivateConstructorPublic()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Node
            {
                private Node() { }

                [Transpile]
                public sealed class Leaf : Node
                {
                    public Node Parent() => new Node();
                }
            }
            """
        );

        await Assert.That(files["node.ts"]).Contains("export class Node {\n  constructor() { }");
        await SingleDiagnostic(diagnostics, DiagnosticCodes.ConstructorAccessibilityWidened);
    }

    [Test]
    public async Task PrivateProtectedConstructorExtendedInTheAssembly_LowersToProtected()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public abstract class Account
            {
                private protected Account() { }
            }

            [Transpile]
            public class Savings : Account { }
            """
        );

        await Assert.That(files["account.ts"]).Contains("  protected constructor() { }");
        await Assert.That(files["savings.ts"]).Contains("export class Savings extends Account {");
    }

    [Test]
    public async Task ProtectedConstructorExtendedByASubclass_StaysProtected()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public abstract class Animal
            {
                protected Animal(string name) { Name = name; }

                public string Name { get; }
            }

            [Transpile]
            public class Dog : Animal
            {
                public Dog() : base("dog") { }
            }
            """
        );

        await Assert
            .That(files["animal.ts"])
            .Contains("  protected constructor(readonly name: string) {");
        await Assert.That(ConstructorAccessDiagnostics(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task Printer_PrefixesEveryOverloadSignatureWithTheModifier()
    {
        var number = new TsNamedType("number");
        var constructor = new TsConstructor(
            [
                new TsConstructorParam(
                    "args",
                    new TsNamedType("unknown[]"),
                    Accessibility: TsAccessibility.None,
                    Rest: true
                ),
            ],
            [],
            [
                new TsConstructorOverload([
                    new TsConstructorParam("x", number, Accessibility: TsAccessibility.None),
                ]),
                new TsConstructorOverload([]),
            ],
            TsAccessibility.Private
        );

        var output = new Printer().Print(
            new TsSourceFile("probe.ts", [new TsClass("Probe", constructor, [])])
        );

        await Assert.That(output).Contains("  private constructor(x: number);\n");
        await Assert.That(output).Contains("  private constructor();\n");
        await Assert.That(output).Contains("  private constructor(...args: unknown[]) {");
    }

    [Test]
    public async Task Facts_FingerprintTheGroupsWhoseConstructorDependsOnAnotherFile()
    {
        var compilation = TranspileHelper.CompileLibrary(
            """
            namespace App;

            [Transpile]
            public abstract class Base { private protected Base() { } }

            [Transpile]
            public class Derived : Base { }

            [Transpile]
            public class Plain { }
            """
        );
        var types = new[] { "App.Base", "App.Derived", "App.Plain" }
            .Select(name => compilation.GetTypeByMetadataName(name)!)
            .ToList();

        var facts = ConstructorAccessFacts.Collect(compilation, types, _ => []);

        await Assert.That(facts.FingerprintOf([types[0]])).IsEqualTo("App.Base:extended");
        await Assert.That(facts.FingerprintOf([types[1], types[2]])).IsEmpty();
    }

    [Test]
    public async Task GroupCache_RetransformsTheBaseWhenAnotherFileStartsExtendingIt()
    {
        // The derived class lives in another group, and the base's closure
        // does not include its dependents: only the constructor facts in the
        // group key invalidate the base's cached `private constructor`.
        var cacheDir = CreateTempDir();

        var first = TransformWithCache(cacheDir, Accounts(derivesFromAccount: false));
        var second = TransformWithCache(cacheDir, Accounts(derivesFromAccount: true));

        await Assert.That(first).Contains("  private constructor() { }");
        await Assert.That(second).Contains("  protected constructor() { }");
    }

    /// <summary>
    /// Transforms <paramref name="source"/> with the per-group cache in
    /// <paramref name="cacheDir"/>, writes the files there the way the host
    /// would, and returns <c>app/account.ts</c> as written.
    /// </summary>
    private static string TransformWithCache(string cacheDir, string source)
    {
        var compilation = TranspileHelper.CompileLibrary(source);
        var (ir, _) = TranspileHelper.NewTransformerWithIr(compilation);
        var printer = new Printer();

        var transformer = new TypeTransformer(ir, compilation)
        {
            CacheOutputDir = cacheDir,
            CacheConfigurationFingerprint = "fp",
        };

        foreach (var file in transformer.TransformAll())
        {
            var path = Path.Combine(cacheDir, file.FileName);
            var content = transformer.CachedFileContents.TryGetValue(file.FileName, out var cached)
                ? cached
                : printer.Print(file);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        return File.ReadAllText(Path.Combine(cacheDir, "app", "account.ts"));
    }

    private static string Accounts(bool derivesFromAccount) =>
        $$"""
            namespace App;

            [Transpile]
            public abstract class Account
            {
                private protected Account() { }
            }

            [Transpile]
            public class Savings{{(derivesFromAccount ? " : Account" : "")}} { }
            """;

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"metano-ctor-access-{Guid.NewGuid():N}");

        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);

        return dir;
    }

    private static string JsonContext(string types, string root) =>
        $$"""
            using System.Text.Json.Serialization;

            namespace App;

            {{types}}

            [Transpile]
            [JsonSourceGenerationOptions]
            [JsonSerializable(typeof({{root}}))]
            public partial class AppJsonContext : JsonSerializerContext
            {
                public AppJsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
                protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
            }
            """;

    private static async Task<MetanoDiagnostic> SingleDiagnostic(
        IEnumerable<MetanoDiagnostic> diagnostics,
        string code
    )
    {
        var matches = diagnostics.Where(d => d.Code == code).ToList();

        await Assert.That(matches).HasSingleItem();

        return matches[0];
    }

    private static List<MetanoDiagnostic> ConstructorAccessDiagnostics(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) =>
        diagnostics
            .Where(d =>
                d.Code
                    is DiagnosticCodes.MixedConstructorAccessibility
                        or DiagnosticCodes.ConstructorAccessibilityWidened
            )
            .ToList();
}
