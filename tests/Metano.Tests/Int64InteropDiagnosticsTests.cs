using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript;

namespace Metano.Tests;

/// <summary>
/// The interop diagnostics of spec 012 slice 012i: <c>MS0065</c>
/// (FR-059, DECIDED-18) fires once per <c>[Import]</c> / <c>[Emit]</c> /
/// <c>[External]</c> declaration and once per user <c>[MapMethod]</c> /
/// <c>[MapProperty]</c> mapping whose signature carries a 64-bit integer,
/// in the staging <see cref="Int64Representation.BigInt"/> mode only, and
/// <c>[PlainObject]</c> 64-bit fields emit <c>bigint</c> with construction
/// conversions (FR-060).
/// </summary>
public class Int64InteropDiagnosticsTests
{
    private const string ImportSource = """
        using System;
        using System.Collections.Generic;
        using Metano.Annotations.TypeScript;

        [Import(name: "Lib", from: "lib")]
        public class Lib
        {
            public long Count() => throw new NotSupportedException();

            public void Send(long id) { }

            public List<long?> Pending() => throw new NotSupportedException();

            public long[] Trail() => throw new NotSupportedException();

            public Func<long[], string> Describe => throw new NotSupportedException();

            public long? Tick { get; }

            public delegate long Mapper(long input);

            public Mapper Map { get; }

            public string Name(long handle) => "";
        }
        """;

    private const string EmitSource = """
        [Transpile]
        public static class Emitter
        {
            [Emit("$0.getItem($1)")]
            public static long GetItem(object cache, long key) => throw new NotSupportedException();

            [Emit("$0.tick()")]
            public static long? Tick(object cache) => throw new NotSupportedException();
        }
        """;

    private const string ExternalSource = """
        using System;
        using System.Collections.Generic;
        using Metano.Annotations.TypeScript;

        [External]
        public interface ICache
        {
            long Size();

            string Name(long handle);

            List<long> Pending();

            Func<long, bool> Filter { get; }

            long? Tick { get; }
        }
        """;

    private const string MemberExternalSource = """
        using System;
        using Metano.Annotations.TypeScript;

        [Transpile]
        public static class Mixed
        {
            [External]
            public static extern long Size();

            public static long Local() => 0;
        }
        """;

    private const string UserMappingSource = """
        using System;

        [assembly: MapMethod(typeof(DateTime), nameof(DateTime.ToBinary), JsMethod = "toBinary")]
        [assembly: MapProperty(typeof(AppClock), nameof(AppClock.Epoch), JsProperty = "epoch")]

        public static class AppClock
        {
            public static long Epoch => 0;
        }
        """;

    /// <summary>
    /// A caller of <c>long.TryParse</c>: the mapping comes from Metano's own
    /// <c>src/Metano/Runtime</c>, which is versioned with the compiler and so
    /// never a user boundary (FR-059).
    /// </summary>
    private const string MetanoMappingUseSource = """
        [Transpile]
        public static class UsesRuntime
        {
            public static bool TryRead(string text)
            {
                long.TryParse(text, out var value);
                return value != 0;
            }
        }
        """;

    /// <summary>
    /// A constructor with a 64-bit parameter is on the surface (FR-059
    /// "member or type"): wherever Metano emits \`new ImportedType(seed)\`,
    /// a bigint crosses into JS-written code. The short name is \`Lib()\`.
    /// </summary>
    [Test]
    public async Task ImportedConstructor_ReportsTheParameter()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations.TypeScript;

            [Import(name: "Lib", from: "lib")]
            public class Lib
            {
                public Lib(long seed) { }

                public long Count() => throw new NotSupportedException();
            }
            """
        );

        var reports = diagnostics
            .Where(d => d.Code == DiagnosticCodes.BigIntAtInteropBoundary)
            .ToList();

        await Assert.That(reports).Count().IsEqualTo(2);

        await Assert.That(reports.Any(d => d.Message.Contains("'Lib()'"))).IsTrue();

        await Assert
            .That(reports.All(d => d.Severity == MetanoDiagnosticSeverity.Warning))
            .IsTrue();
    }

    /// <summary>
    /// The pointer-width forms (DECIDED-2: nint follows every rule of the
    /// spec) report like their 64-bit namesakes.
    /// </summary>
    [Test]
    public async Task NativeIntegerForms_Report()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;
            using Metano.Annotations.TypeScript;

            [Import(name: "Alloc", from: "alloc")]
            public static class Alloc
            {
                public static extern nint Reserve(long bytes);

                public static nuint Size => (nuint)8;
            }
            """
        );

        var reports = diagnostics
            .Where(d => d.Code == DiagnosticCodes.BigIntAtInteropBoundary)
            .ToList();

        await Assert.That(reports).Count().IsEqualTo(2);
    }

    [Test]
    public async Task ImportMember_ReportsEachDeclarationOnce()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(ImportSource);

        var reports = diagnostics
            .Where(d => d.Code == DiagnosticCodes.BigIntAtInteropBoundary)
            .ToList();

        // One per declaration: Count, Send, Pending, Trail, Describe, Tick,
        // Mapper (the delegate's own Invoke), Map, Name — not once per form.
        await Assert.That(reports).Count().IsEqualTo(9);

        await Assert
            .That(reports.All(d => d.Severity == MetanoDiagnosticSeverity.Warning))
            .IsTrue();
    }

    [Test]
    public async Task ImportMember_ReturnAndParameterMessages_NameTheJavaScriptSide()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(ImportSource);

        var reports = diagnostics
            .Where(d => d.Code == DiagnosticCodes.BigIntAtInteropBoundary)
            .ToList();

        // The contract's exact example shape, at the member the warning names.
        await Assert
            .That(reports.Single(d => d.Message.Contains("'Lib.Count()'")).Message)
            .IsEqualTo(
                "'Lib.Count()' returns 'long', which is a bigint in TypeScript; the "
                    + "imported JavaScript must return a bigint, not a number "
                    + "(spec 012 FR-059)."
            );

        await Assert
            .That(reports.Single(d => d.Message.Contains("'Lib.Send()'")).Message)
            .IsEqualTo(
                "'Lib.Send()' parameter 'id' is 'long', which is a bigint in TypeScript; "
                    + "the imported JavaScript must receive a bigint, not a number "
                    + "(spec 012 FR-059)."
            );
    }

    [Test]
    public async Task ImportMember_CoversNullableArrayGenericAndDelegateForms()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(ImportSource);

        var reports = diagnostics
            .Where(d => d.Code == DiagnosticCodes.BigIntAtInteropBoundary)
            .ToList();

        await Assert
            .That(reports.Single(d => d.Message.Contains("'Lib.Pending()'")).Message)
            .Contains("returns 'System.Collections.Generic.List<long?>'");

        await Assert
            .That(reports.Single(d => d.Message.Contains("'Lib.Trail()'")).Message)
            .Contains("returns 'long[]'");

        await Assert
            .That(reports.Single(d => d.Message.Contains("'Lib.Describe'")).Message)
            .Contains("'System.Func<long[], string>'");

        await Assert
            .That(reports.Single(d => d.Message.Contains("'Lib.Tick'")).Message)
            .Contains("has property type 'long?'");

        // A custom delegate parameter of a property counts through its Invoke.
        await Assert
            .That(reports.Single(d => d.Message.Contains("'Lib.Map'")).Message)
            .Contains("has property type 'Lib.Mapper'");
    }

    [Test]
    public async Task ImportMember_Locations_PointAtTheDeclarations()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(ImportSource);

        var reports = diagnostics
            .Where(d => d.Code == DiagnosticCodes.BigIntAtInteropBoundary)
            .ToList();

        await Assert
            .That(LocatedText(reports.Single(d => d.Message.Contains("'Lib.Count()'"))))
            .IsEqualTo("Count");
        await Assert
            .That(LocatedText(reports.Single(d => d.Message.Contains("'Lib.Tick'"))))
            .IsEqualTo("Tick");
    }

    [Test]
    public async Task EmitMember_ReportsTheEmittedSignature()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(EmitSource);

        var reports = diagnostics
            .Where(d => d.Code == DiagnosticCodes.BigIntAtInteropBoundary)
            .ToList();

        await Assert.That(reports).Count().IsEqualTo(2);

        await Assert
            .That(reports.Single(d => d.Message.Contains("'Emitter.GetItem()'")).Message)
            .IsEqualTo(
                "'Emitter.GetItem()' returns 'long', which is a bigint in TypeScript; "
                    + "the emitted JavaScript must return a bigint, not a number "
                    + "(spec 012 FR-059)."
            );

        await Assert
            .That(reports.Single(d => d.Message.Contains("'Emitter.Tick()'")).Message)
            .Contains("returns 'long?'");
    }

    [Test]
    public async Task ExternalType_ReportsEveryMemberSignature()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(ExternalSource);

        var reports = diagnostics
            .Where(d => d.Code == DiagnosticCodes.BigIntAtInteropBoundary)
            .ToList();

        // Size, Name, Pending, Filter, Tick — once per declaration.
        await Assert.That(reports).Count().IsEqualTo(5);

        await Assert.That(reports.All(d => d.Message.Contains("the external JavaScript"))).IsTrue();

        await Assert
            .That(reports.Single(d => d.Message.Contains("'ICache.Filter'")).Message)
            .Contains("has property type 'System.Func<long, bool>'");
    }

    [Test]
    public async Task MemberLevelExternal_ReportsOnlyTheMarkedMember()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(MemberExternalSource);

        var reports = diagnostics
            .Where(d => d.Code == DiagnosticCodes.BigIntAtInteropBoundary)
            .ToList();

        await Assert.That(reports).Count().IsEqualTo(1);

        await Assert
            .That(reports[0].Message)
            .IsEqualTo(
                "'Mixed.Size()' returns 'long', which is a bigint in TypeScript; the "
                    + "external JavaScript must return a bigint, not a number "
                    + "(spec 012 FR-059)."
            );
    }

    [Test]
    public async Task UserMapping_ReportsOncePerMappingAtTheAttribute()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(UserMappingSource);

        var reports = diagnostics
            .Where(d => d.Code == DiagnosticCodes.BigIntAtInteropBoundary)
            .ToList();

        await Assert.That(reports).Count().IsEqualTo(2);

        var method = reports.Single(d => d.Message.Contains("[MapMethod]"));

        await Assert
            .That(method.Message)
            .IsEqualTo(
                "[MapMethod] 'DateTime.ToBinary()' returns 'long', which is a bigint "
                    + "in TypeScript; the mapped JavaScript must return a bigint, not "
                    + "a number (spec 012 FR-059)."
            );

        var property = reports.Single(d => d.Message.Contains("[MapProperty]"));

        await Assert
            .That(property.Message)
            .IsEqualTo(
                "[MapProperty] 'AppClock.Epoch' has property type 'long', which is a "
                    + "bigint in TypeScript; the mapped JavaScript exchanges a bigint "
                    + "with it, not a number (spec 012 FR-059)."
            );

        await Assert.That(LocatedText(method)).Contains("MapMethod");
        await Assert.That(LocatedText(property)).Contains("MapProperty");
    }

    [Test]
    public async Task MetanoRuntimeMappings_AreExcluded()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(MetanoMappingUseSource);

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.BigIntAtInteropBoundary))
            .IsFalse();
    }

    [Test]
    public async Task NumberMode_ReportsNothing()
    {
        var (_, importDiagnostics) = TranspileHelper.TranspileWithDiagnostics(ImportSource);

        var (_, emitDiagnostics) = TranspileHelper.TranspileWithDiagnostics(EmitSource);

        var (_, externalDiagnostics) = TranspileHelper.TranspileWithDiagnostics(ExternalSource);

        var (_, mappingDiagnostics) = TranspileHelper.TranspileWithDiagnostics(UserMappingSource);

        foreach (
            var diagnostics in new[]
            {
                importDiagnostics,
                emitDiagnostics,
                externalDiagnostics,
                mappingDiagnostics,
            }
        )
        {
            await Assert
                .That(diagnostics.Any(d => d.Code == DiagnosticCodes.BigIntAtInteropBoundary))
                .IsFalse();
        }
    }

    /// <summary>
    /// Several 64-bit forms on one declaration warn once: the message names
    /// the first position in the fixed order (return, then parameters).
    /// </summary>
    [Test]
    public async Task OncePerDeclaration_WithSeveralSixtyFourBitForms()
    {
        const string source = """
            using System;
            using System.Collections.Generic;

            [Transpile]
            public static class Wide
            {
                [Emit("$0.run($1, $2, $3)")]
                public static long Run(object cache, long[] trail, List<long?> pending, Func<long, int> pick)
                    => throw new NotSupportedException();
            }
            """;

        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(source);

        var reports = diagnostics
            .Where(d => d.Code == DiagnosticCodes.BigIntAtInteropBoundary)
            .ToList();

        await Assert.That(reports).Count().IsEqualTo(1);

        await Assert.That(reports[0].Message).Contains("returns 'long'");
    }

    /// <summary>
    /// The warning changes no emitted call: an <c>[Import]</c> / <c>[Emit]</c>
    /// call site passes the <c>bigint</c> through and converts only what C#'s
    /// own conversion rules convert (FR-059, FR-010) — no <c>bigint</c> ↔
    /// <c>number</c> coercion is inserted.
    /// </summary>
    [Test]
    public async Task CallSites_PassBigintsThroughWithoutCoercion()
    {
        const string source = """
            [Transpile]
            public static class Calls
            {
                [Import("count", from: "lib")]
                public static extern long Count();

                [Emit("$0.take($1)")]
                public static void Take(object cache, long value) => throw new NotSupportedException();

                public static long Read(int seed)
                {
                    Take(null, seed);
                    return Count();
                }
            }
            """;

        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(source);

        var output = files["calls.ts"];

        // The int seed widens at the call (C#'s own int → long conversion);
        // the bigint flows through untouched.
        await Assert.That(output).Contains("BigInt(seed)");
        await Assert.That(output).Contains("return count();");

        await Assert
            .That(
                diagnostics.Where(d => d.Code == DiagnosticCodes.BigIntAtInteropBoundary).ToList()
            )
            .Count()
            .IsEqualTo(2);
    }

    /// <summary>
    /// FR-060: a 64-bit <c>[PlainObject]</c> field is <c>bigint</c> in every
    /// field position (required, defaulted <c>?:</c>, nullable, array), and
    /// the object literal a <c>new T(args)</c> lowers to converts its
    /// arguments per FR-010: constants fold, an <c>int</c> widens, a
    /// <c>long</c> flows through, <c>null</c> stays <c>null</c>, and an
    /// omitted defaulted argument keeps its key out.
    /// </summary>
    [Test]
    public async Task BigIntMode_PlainObject_MatchesGolden()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(PlainObjectSource);

        await Assert
            .That(files["snowflake-dto.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("int64-plain-object.ts"));

        await Assert
            .That(files["builder.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("int64-plain-object-builder.ts"));
    }

    private const string PlainObjectSource = """
        using System;

        [Transpile, PlainObject]
        public record SnowflakeDto(long Id, ulong Mask, long? Parent, long[] Trail, long Ticks = 1L << 40);

        [Transpile]
        public static class Builder
        {
            public static SnowflakeDto MakeConst() => new(1L << 40, 7UL, null, new long[] { 1, 2 });

            public static SnowflakeDto MakeWide(int id, ulong mask, long? parent, long[] trail) =>
                new(id, mask, parent, trail, 5);

            public static SnowflakeDto MakeBare(long id, ulong mask, long? parent, long[] trail) =>
                new(id, mask, parent, trail);
        }
        """;

    private static string LocatedText(MetanoDiagnostic diagnostic) =>
        diagnostic.Location!.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);
}
