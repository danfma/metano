using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The function dispatchers (issue #569). The groups of a containerless
/// class, of an extension container and of a branded companion namespace
/// get the two <c>MS0063</c> warnings the class dispatchers get. Every
/// dispatched group of a companion namespace exports its per-overload
/// functions, and a call or method group transpiled from C# names the
/// function of the overload C# bound instead of the dispatcher, whose
/// runtime checks cannot tell every pair of overloads apart.
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

    /// <summary>
    /// The names a call site must agree with the namespace on: a fast path
    /// that collides with another static method takes a <c>$</c>, a
    /// <c>[Name]</c> pins an overload's suffix, and an overload that is not
    /// public keeps its <c>@internal</c> tag on the exported function.
    /// </summary>
    private const string Names = """
        [Branded]
        public readonly record struct Key(string Value)
        {
            public static string X(long l) => "x-long";

            public static string X(UId u) => "x-uid";

            public static string XL(string s) => "xl";

            [Name("viaLong")]
            public static string Pin(long l) => "long";

            public static string Pin(UId u) => "uid";

            public static string V(long l) => "long";

            internal static string V(UId u) => "internal";

            private static string V(string s) => "private";

            public static string Hidden() => V("s");

            public static string D(long l, int n = 3) => "long" + n;

            public static string D(UId u, int n = 5) => "uid" + n;
        }

        public class KeyCalls
        {
            private int _counter;

            private int Next() => ++_counter;

            private UId NextId() => new(++_counter);

            public string Run() =>
                Key.X(1L)
                + Key.X(new UId(1))
                + Key.XL("s")
                + Key.Pin(1L)
                + Key.Pin(new UId(1))
                + Key.V(new UId(1))
                + Key.D(new UId(1))
                + Key.D(n: Next(), u: NextId());
        }
        """;

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

        await Assert.That(warning.Location).IsNotNull();
        await Assert.That(warning.Location!.SourceTree).IsNotNull();

        var location = warning.Location;
        var compiled = location.SourceTree!.GetText().ToString();
        var anchorStart = compiled.IndexOf(anchor, StringComparison.Ordinal);

        await Assert.That(anchorStart).IsGreaterThanOrEqualTo(0);
        await Assert.That(location.SourceSpan.Start).IsEqualTo(anchorStart);
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
            AmbiguousDispatchMessages.BrandedSixtyFourBit("'Mod.H(long)', 'Mod.H(UId)'"),
            "H(UId u)"
        );

    [Test]
    public Task NoContainerFunctions_BrandedBesideUlong_WarnAtTheBrandedOverload() =>
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
            AmbiguousDispatchMessages.BrandedSixtyFourBit("'Bare.H(UId)', 'Bare.H(ulong)'"),
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
            AmbiguousDispatchMessages.BrandedSixtyFourBit(
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
            AmbiguousDispatchMessages.BrandedSixtyFourBit("'Tag.S(long)', 'Tag.S(UId)'"),
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
            AmbiguousDispatchMessages.CatchAll("Mod.Any<T>(T)", "Mod.Any(int, int)"),
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
            AmbiguousDispatchMessages.CatchAll("Tag.Any<T>(T)", "Tag.Any(int, int)"),
            "Any<T>(T value)"
        );

    [Test]
    public Task CompanionNamespace_OutParameters_NameTheRealSignatures() =>
        AssertWarnsOnceAt(
            Preamble
                + """
                [Branded]
                public readonly record struct Tag(string Value)
                {
                    public static string Out(long l, out int n) { n = 1; return "long"; }

                    public static string Out(UId u, out int n) { n = 2; return "uid"; }
                }
                """,
            AmbiguousDispatchMessages.BrandedSixtyFourBit(
                "'Tag.Out(long, out int)', 'Tag.Out(UId, out int)'"
            ),
            "Out(UId u, out int n)"
        );

    [Test]
    public Task ConstructorGroup_OutParameters_NameTheRealSignatures() =>
        AssertWarnsOnceAt(
            Preamble
                + """
                public class BrandCtor
                {
                    public BrandCtor(long a, out int r) { r = 1; }

                    public BrandCtor(UId a, out int r) { r = 2; }
                }
                """,
            AmbiguousDispatchMessages.BrandedSixtyFourBit(
                "'BrandCtor.BrandCtor(long, out int)', 'BrandCtor.BrandCtor(UId, out int)'"
            ),
            "BrandCtor(UId a, out int r)"
        );

    [Test]
    public Task CompanionNamespace_OperatorGroup_SaysItsApplicationsAreNotLowered() =>
        AssertWarnsOnceAt(
            Preamble
                + """
                [Branded]
                public readonly record struct Ticks(long Value)
                {
                    public static Ticks operator +(Ticks a, long b) => new(a.Value + b);

                    public static Ticks operator +(Ticks a, UId b) => new(a.Value + b.Value);
                }
                """,
            AmbiguousDispatchMessages.BrandedSixtyFourBitOperators(
                "'Ticks.operator +(Ticks, long)', 'Ticks.operator +(Ticks, UId)'"
            ),
            "+(Ticks a, long b)"
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
    public async Task CompanionNamespace_DispatchedGroups_ExportTheirOverloadFunctions()
    {
        var result = TranspileHelper.Transpile(Preamble + Companion + Calls);

        await TranspileHelper.AssertGolden("companion-direct-overloads-tag.ts", result["tag.ts"]);
    }

    [Test]
    public async Task CallsIntoACompanionGroup_NameTheOverloadCSharpBound()
    {
        var result = TranspileHelper.Transpile(Preamble + Companion + Calls);

        await TranspileHelper.AssertGolden(
            "companion-direct-overloads-calls.ts",
            result["calls.ts"]
        );
    }

    [Test]
    public async Task CompanionNamespace_ClaimedNames_KeepCollisionsPinsAndVisibility()
    {
        var result = TranspileHelper.Transpile(Preamble + Names);

        await TranspileHelper.AssertGolden("companion-direct-overloads-key.ts", result["key.ts"]);
    }

    [Test]
    public async Task CallsIntoACompanionGroup_SpellTheClaimedNamesAndKeepArgumentOrder()
    {
        var result = TranspileHelper.Transpile(Preamble + Names);

        await TranspileHelper.AssertGolden(
            "companion-direct-overloads-key-calls.ts",
            result["key-calls.ts"]
        );
    }

    [Test]
    public async Task BrandedWrapperOverAClass_BindsItsClassFastPaths()
    {
        var result = TranspileHelper.Transpile(
            Preamble
                + """
                public record Payload(int X);

                [Branded]
                public readonly record struct Wrap(Payload Value)
                {
                    public static string S(long l) => "long";

                    public static string S(UId u) => "uid";
                }

                public class WrapCalls
                {
                    public string Run() => Wrap.S(new UId(1)) + Wrap.S(1L);
                }
                """
        );

        await TranspileHelper.AssertGolden(
            "companion-direct-overloads-class-wrapper-calls.ts",
            result["wrap-calls.ts"]
        );
    }

    [Test]
    public async Task CompanionNamespace_RenamedExportedFastPath_ReportsMs0067()
    {
        var source =
            Preamble
            + """
                [Branded]
                public readonly record struct UserId(string Value)
                {
                    public static string From(string raw) => "string";

                    public static string From(UserId id) => "userid";

                    public static string FromRaw(int n) => "int";
                }
                """;

        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(source);

        var rename = diagnostics.Single(d => d.Code == DiagnosticCodes.OverloadNameShadowed);

        await Assert.That(rename.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);

        await Assert
            .That(rename.Message)
            .IsEqualTo(
                "Fast path 'UserId.fromRaw' of 'From(string raw)' is already taken in the "
                    + "companion namespace; it is emitted as 'fromRaw$' (spec 013 FR-004)."
            );

        await Assert.That(rename.Location).IsNotNull();
        await Assert.That(rename.Location!.SourceTree).IsNotNull();

        var compiled = rename.Location.SourceTree!.GetText().ToString();
        var declaration = compiled.IndexOf("From(string raw)", StringComparison.Ordinal);

        await Assert.That(declaration).IsGreaterThanOrEqualTo(0);
        await Assert.That(rename.Location.SourceSpan.Start).IsEqualTo(declaration);

        await TranspileHelper.AssertGolden(
            "companion-direct-overloads-renamed.ts",
            files["user-id.ts"]
        );
    }

    [Test]
    public async Task CompanionNamespace_OperatorGroup_KeepsItsFastPathsLocal()
    {
        var result = TranspileHelper.Transpile(
            Preamble
                + """
                [Branded]
                public readonly record struct Ticks(long Value)
                {
                    public static Ticks operator +(Ticks a, long b) => new(a.Value + b);

                    public static Ticks operator +(Ticks a, UId b) => new(a.Value + b.Value);
                }
                """
        );

        await TranspileHelper.AssertGolden(
            "companion-direct-overloads-operators.ts",
            result["ticks.ts"]
        );
    }

    /// <summary>
    /// A primitive-backed wrapper's companion declares no instance member,
    /// so a call into an overloaded instance group is not bound: it keeps its
    /// source arguments (no explicit default) and the member name.
    /// </summary>
    [Test]
    public async Task InstanceGroupOfAPrimitiveBackedWrapper_IsNotBound()
    {
        var result = TranspileHelper.Transpile(
            Preamble
                + """
                [Branded]
                public readonly record struct Tag(string Value)
                {
                    public string I(long l, int n = 3) => "long" + n;

                    public string I(UId u, int n = 5) => "uid" + n;
                }

                public class InstanceCalls
                {
                    public string Run(Tag tag) => tag.I(1L) + tag.I(new UId(1));
                }
                """
        );

        await TranspileHelper.AssertGolden(
            "companion-direct-overloads-instance-calls.ts",
            result["instance-calls.ts"]
        );
    }

    /// <summary>
    /// An operator of a branded type is not bound to a companion function
    /// (#599): the application keeps the lowering it has without one.
    /// </summary>
    [Test]
    public async Task OperatorOfABrandedType_IsNotBound()
    {
        var result = TranspileHelper.Transpile(
            Preamble
                + """
                [Branded]
                public readonly record struct Ticks(long Value)
                {
                    public static Ticks operator +(Ticks a, long b) => new(a.Value + b);

                    public static Ticks operator +(Ticks a, UId b) => new(a.Value + b.Value);
                }

                public class OperatorCalls
                {
                    public Ticks Run(Ticks ticks, UId id) => ticks + 2L + id;
                }
                """
        );

        await TranspileHelper.AssertGolden(
            "companion-direct-overloads-operator-calls.ts",
            result["operator-calls.ts"]
        );
    }

    /// <summary>
    /// The Dart target declares one member per group of a branded type and
    /// leaves <c>EmitsBrandedFastPath</c> unset: the call keeps its source
    /// form, named arguments included, instead of a fast-path name Dart
    /// never declares.
    /// </summary>
    [Test]
    public async Task Dart_KeepsTheSourceCallIntoABrandedGroup()
    {
        var (files, _) = TranspileHelper.TranspileDart(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [Branded]
            public readonly record struct UId(long Value);

            [Branded]
            public readonly record struct Tag(string Value)
            {
                public static string D(long l, int n = 3, string s = "d") => "long";

                public static string D(UId u, int n = 5, string s = "e") => "uid";
            }

            public class DartCalls
            {
                public string Run(UId uid) => Tag.D(u: uid, n: 7);
            }
            """
        );

        await TranspileHelper.AssertGolden(
            "companion-direct-overloads-dart-calls.dart",
            files["dart_calls.dart"]
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
