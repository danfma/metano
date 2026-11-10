using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// <c>MS0063</c> on the function dispatchers (issue #569): the groups of an
/// <c>[ExportedAsModule]</c> class, of an extension container and of a
/// branded companion namespace get the two warnings the class dispatchers
/// get. A companion group either detector finds ambiguous also exports its
/// per-overload functions, and a call transpiled from C# names the overload
/// C# bound instead of the dispatcher, which would run another one.
/// </summary>
public class FunctionDispatcherAmbiguityTests
{
    private const string Preamble = """
        using System;
        using Metano.Annotations;
        [assembly: TranspileAssembly]

        [Branded]
        public readonly record struct UId(long Value);

        [Branded]
        public readonly record struct Count(int Value);

        """;

    private const string Companion = """
        [Branded]
        public readonly record struct Tag(string Value)
        {
            public static string S(long l) => "long";

            public static string S(UId u) => "uid";

            public static string Any<T>(T value) => "any";

            public static string Any(int a, int b = 0) => "int";

            public static string N(string s) => "string";

            public static string N(int i) => "int";

            public static string Both() => S(new UId(2)) + S(2L);
        }

        """;

    private const string Calls = """
        public class Calls
        {
            public string Run()
            {
                Func<UId, string> pick = Tag.S;

                return Tag.S(new UId(1))
                    + Tag.S(1L)
                    + pick(new UId(3))
                    + Tag.Any("x")
                    + Tag.Any(1)
                    + Tag.N("a")
                    + Tag.N(1);
            }
        }
        """;

    private static string CatchAllMessageFor(string catchAll, string shadowed) =>
        $"Overload '{catchAll}' accepts any argument at runtime, so the TypeScript dispatcher "
        + $"tries it before '{shadowed}', whose argument counts it overlaps: a call C# binds to "
        + "one of them can reach it instead (ambiguous runtime dispatch). Static call-site "
        + "dispatch (#25) will resolve it; until then, give the overloads distinct arities or "
        + "constrain the type parameter.";

    private static IReadOnlyList<MetanoDiagnostic> Ambiguity(string source) =>
        TranspileHelper
            .TranspileWithDiagnostics(source)
            .Diagnostics.Where(d => d.Code == DiagnosticCodes.AmbiguousRuntimeDispatch)
            .ToList();

    /// <summary>
    /// The one warning of <paramref name="source"/> has exactly
    /// <paramref name="message"/> and sits on the method name that starts
    /// the declaration text <paramref name="anchor"/>.
    /// </summary>
    private static async Task AssertWarnsOnceAt(string source, string message, string anchor)
    {
        var warning = Ambiguity(source).Single();

        await Assert.That(warning.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(warning.Message).IsEqualTo(message);

        var location = warning.Location!;
        var compiled = location.SourceTree!.GetText().ToString();

        await Assert.That(location.SourceSpan.Start).IsEqualTo(compiled.IndexOf(anchor));
    }

    [Test]
    public Task ModuleFunctions_BrandedBesideLong_WarnAtTheBrandedOverload() =>
        AssertWarnsOnceAt(
            Preamble
                + """
                [ExportedAsModule]
                public static class Mod
                {
                    public static string H(long l) => "long";

                    public static string H(UId u) => "uid";
                }
                """,
            Int64BrandedDispatchDiagnosticTests.MessageFor("'Mod.H(long)', 'Mod.H(UId)'"),
            "H(UId u)"
        );

    [Test]
    public Task NoContainerFunctions_BrandedBesideLong_WarnAtTheBrandedOverload() =>
        AssertWarnsOnceAt(
            Preamble
                + """
                [NoContainer]
                public static class Bare
                {
                    public static string H(UId u) => "uid";

                    public static string H(ulong l) => "ulong";
                }
                """,
            Int64BrandedDispatchDiagnosticTests.MessageFor("'Bare.H(UId)', 'Bare.H(ulong)'"),
            "H(UId u)"
        );

    [Test]
    public Task ExtensionMethods_BrandedBesideLong_WarnAtTheBrandedOverload() =>
        AssertWarnsOnceAt(
            Preamble
                + """
                public static class Ext
                {
                    public static string E(this string s, long l) => "long";

                    public static string E(this string s, UId u) => "uid";
                }
                """,
            Int64BrandedDispatchDiagnosticTests.MessageFor(
                "'Ext.E(string, long)', 'Ext.E(string, UId)'"
            ),
            "E(this string s, UId u)"
        );

    [Test]
    public Task CompanionNamespace_BrandedBesideLong_WarnsAtTheBrandedOverload() =>
        AssertWarnsOnceAt(
            Preamble
                + """
                [Branded]
                public readonly record struct Tag(string Value)
                {
                    public static string S(long l) => "long";

                    public static string S(UId u) => "uid";
                }
                """,
            Int64BrandedDispatchDiagnosticTests.MessageFor("'Tag.S(long)', 'Tag.S(UId)'"),
            "S(UId u)"
        );

    [Test]
    public Task ModuleFunctions_CatchAllBeforeAConcreteOverload_WarnAtTheCatchAll() =>
        AssertWarnsOnceAt(
            Preamble
                + """
                [ExportedAsModule]
                public static class Mod
                {
                    public static string Any<T>(T value) => "any";

                    public static string Any(int a, int b = 0) => "int";
                }
                """,
            CatchAllMessageFor("Mod.Any<T>(T)", "Mod.Any(int, int)"),
            "Any<T>(T value)"
        );

    [Test]
    public Task CompanionNamespace_CatchAllBeforeAConcreteOverload_WarnsAtTheCatchAll() =>
        AssertWarnsOnceAt(
            Preamble
                + """
                [Branded]
                public readonly record struct Tag(string Value)
                {
                    public static string Any<T>(T value) => "any";

                    public static string Any(int a, int b = 0) => "int";
                }
                """,
            CatchAllMessageFor("Tag.Any<T>(T)", "Tag.Any(int, int)"),
            "Any<T>(T value)"
        );

    [Test]
    public async Task FunctionDispatchers_WithoutAnAmbiguousGroup_ReportNothing()
    {
        var warnings = Ambiguity(
            Preamble
                + """
                [ExportedAsModule]
                public static class Mod
                {
                    public static string H(long l) => "long";

                    public static string H(ulong u) => "ulong";

                    public static string K(long l) => "long";

                    public static string K(Count c) => "count";
                }

                public static class Ext
                {
                    public static string E(this string s, UId u) => "uid";

                    public static string E(this string s, int i) => "int";
                }

                [Branded]
                public readonly record struct Tag(string Value)
                {
                    public static string N(string s) => "string";

                    public static string N(int i) => "int";

                    public static string K(long l) => "long";

                    public static string K(Count c) => "count";
                }
                """
        );

        await Assert.That(warnings).IsEmpty();
    }

    [Test]
    public async Task CompanionNamespace_AmbiguousGroups_ExportTheirOverloadFunctions()
    {
        var result = TranspileHelper.Transpile(Preamble + Companion + Calls);

        await TranspileHelper.AssertGolden("companion-direct-overloads-tag.ts", result["tag.ts"]);
    }

    [Test]
    public async Task CallsIntoAnAmbiguousCompanionGroup_NameTheOverloadCSharpBound()
    {
        var result = TranspileHelper.Transpile(Preamble + Companion + Calls);

        await TranspileHelper.AssertGolden(
            "companion-direct-overloads-calls.ts",
            result["calls.ts"]
        );
    }

    [Test]
    public async Task CallsFromAReferencingProject_NameTheOverloadCSharpBound()
    {
        var result = TranspileHelper.TranspileWithLibrary(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("tags")]

            namespace Tags;

            [Branded]
            public readonly record struct UId(long Value);

            [Branded]
            public readonly record struct Tag(string Value)
            {
                public static string S(long l) => "long";

                public static string S(UId u) => "uid";

                public static string N(string s) => "string";

                public static string N(int i) => "int";
            }
            """,
            """
            using Metano.Annotations;
            using Tags;
            [assembly: TranspileAssembly]

            public class Consumer
            {
                public string Run() => Tag.S(new UId(1)) + Tag.S(1L) + Tag.N(1);
            }
            """
        );

        await TranspileHelper.AssertGolden(
            "companion-direct-overloads-consumer.ts",
            result["consumer.ts"]
        );
    }
}
