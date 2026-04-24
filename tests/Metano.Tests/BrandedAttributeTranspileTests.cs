namespace Metano.Tests;

/// <summary>
/// Tests for <c>[Branded]</c> from <c>Metano.Annotations</c>. The
/// attribute is the successor of <c>[InlineWrapper]</c> and carries
/// identical semantics — value-like struct lowers to a branded
/// primitive + companion namespace in TypeScript. These tests pin
/// the renaming contract: any call site that used to work with
/// <c>[InlineWrapper]</c> now works identically with <c>[Branded]</c>,
/// and mixing the two in the same compilation is legal for as long
/// as the predecessor remains supported.
/// </summary>
public class BrandedAttributeTranspileTests
{
    [Test]
    public async Task Branded_GeneratesBrandedTypeAndNamespace()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            [Transpile, Branded]
            public readonly record struct UserId(string Value)
            {
                public static UserId System() => new("system");
            }
            """
        );

        var output = result["user-id.ts"];
        await Assert
            .That(output)
            .Contains("export type UserId = string & { readonly __brand: \"UserId\" };");
        await Assert.That(output).Contains("export namespace UserId");
        await Assert.That(output).Contains("function create(value: string): UserId");
        await Assert.That(output).Contains("function system(): UserId");
        await Assert.That(output).DoesNotContain("class UserId");
    }

    [Test]
    public async Task Branded_NonStringPrimitive_IncludesToString()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            [Transpile, Branded]
            public readonly record struct Amount(int Value);
            """
        );

        var output = result["amount.ts"];
        await Assert
            .That(output)
            .Contains("export type Amount = number & { readonly __brand: \"Amount\" };");
        await Assert.That(output).Contains("function create(value: number): Amount");
        await Assert.That(output).Contains("function toString(value: Amount): string");
    }

    [Test]
    public async Task Branded_NewCall_IsRewrittenToCreate()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [Branded]
            public readonly record struct OrderId(string Value);

            public class Factory
            {
                public OrderId Make() => new OrderId("ord-1");
            }
            """
        );

        var output = result["factory.ts"];
        await Assert.That(output).Contains("OrderId.create(\"ord-1\")");
        await Assert.That(output).DoesNotContain("new OrderId");
    }

    [Test]
    public async Task Branded_AndInlineWrapper_BehaveIdentically()
    {
        // Same struct body, one marked [Branded] the other
        // [InlineWrapper] — generated output must match byte-for-byte
        // (modulo the type name) so the rename is a safe no-op for
        // in-flight callers.
        var branded = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            [Transpile, Branded]
            public readonly record struct BTag(string Value);
            """
        );
        var legacy = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            [Transpile, InlineWrapper]
            public readonly record struct LTag(string Value);
            """
        );

        var brandedOutput = branded["b-tag.ts"].Replace("BTag", "WrapperTag");
        var legacyOutput = legacy["l-tag.ts"].Replace("LTag", "WrapperTag");
        await Assert.That(brandedOutput).IsEqualTo(legacyOutput);
    }
}
