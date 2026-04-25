namespace Metano.Tests;

/// <summary>
/// Regression coverage for issue #119: static members declared on a
/// record type were silently dropped from the generated TypeScript
/// output. The extractor walks every member by symbol kind, but the
/// downstream record-specific class emitter must surface static
/// fields, properties, and methods exactly the same way it does for
/// plain classes — otherwise call sites that reference
/// <c>Counter.Zero</c> or similar resolve to <c>undefined</c> at
/// runtime.
/// </summary>
public class RecordStaticMembersTests
{
    [Test]
    public async Task RecordWithStaticReadonlyField_EmitsFieldOnGeneratedClass()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed record Counter(int Count)
            {
                public static readonly Counter Zero = new(0);
            }
            """
        );

        var output = result["counter.ts"];
        // Field name is camelCased per the TypeScript naming policy
        // (matches static method emission); the static modifier and
        // the field initializer must both survive the round-trip.
        await Assert.That(output).Contains("static readonly zero");
        await Assert.That(output).Contains("new Counter(0)");
    }

    [Test]
    public async Task RecordWithStaticMethod_EmitsMethodOnGeneratedClass()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed record Counter(int Count)
            {
                public static Counter Increment(Counter c) => new(c.Count + 1);
            }
            """
        );

        var output = result["counter.ts"];
        await Assert.That(output).Contains("static increment(c: Counter): Counter");
    }

    [Test]
    public async Task RecordWithStaticProperty_EmitsPropertyOnGeneratedClass()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed record Counter(int Count)
            {
                public static Counter Initial => new(0);
            }
            """
        );

        var output = result["counter.ts"];
        await Assert.That(output).Contains("static get initial(): Counter");
    }

    [Test]
    public async Task PlainObjectRecordWithStaticField_EmitsTopLevelExportConst()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [PlainObject]
            public sealed record Counter(int Count)
            {
                public static readonly Counter Zero = new(0);
            }
            """
        );

        var output = result["counter.ts"];
        // PlainObject records don't get a TS class wrapper; static
        // members surface as top-level exports in the same module
        // under the same camelCased naming policy as the interface
        // members.
        await Assert.That(output).Contains("export const zero");
    }

    [Test]
    public async Task PlainObjectRecordWithReservedNameOverride_EscapesIdentifier()
    {
        // `[Name("delete")]` (or any reserved-word override) cannot
        // surface verbatim as a top-level `export const` — the TS
        // parser rejects reserved identifiers in declaration
        // position. The bridge appends a trailing underscore to
        // keep the emitted file compilable.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [PlainObject]
            public sealed record Counter(int Count)
            {
                [Name("delete")]
                public static readonly string Marker = "x";
            }
            """
        );

        var output = result["counter.ts"];
        await Assert.That(output).Contains("export const delete_");
    }

    [Test]
    public async Task PlainObjectRecordWithPrivateStaticField_DoesNotExport()
    {
        // Top-level exports leak the symbol to every consumer of the
        // module. Private/protected static fields stay confined to
        // C#; nothing about a `[PlainObject]` wire shape implies
        // those should suddenly become module-public.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [PlainObject]
            public sealed record Counter(int Count)
            {
                private static readonly string Internal = "secret";
            }
            """
        );

        var output = result["counter.ts"];
        await Assert.That(output).DoesNotContain("internal");
        await Assert.That(output).DoesNotContain("\"secret\"");
    }

    [Test]
    public async Task PlainObjectRecordWithMutableStaticField_EmitsTopLevelExportLet()
    {
        // Mutable static fields (no `readonly`) lower to a `let`
        // declaration so call sites can still rebind the symbol —
        // matching the C# semantics that a `static int` on a record
        // can be reassigned from anywhere with access.
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [PlainObject]
            public sealed record Counter(int Count)
            {
                public static int Cached = 0;
            }
            """
        );

        var output = result["counter.ts"];
        await Assert.That(output).Contains("export let cached");
    }
}
