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
        // members surface as top-level exports in the same module.
        await Assert.That(output).Contains("export const Zero");
    }
}
