namespace MetaSharp.Tests;

public class InlineWrapperTranspileTests
{
    [Test]
    public async Task InlineWrapper_GeneratesBrandedTypeAndNamespace()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, InlineWrapper]
            public readonly record struct UserId(string Value)
            {
                public static UserId System() => new("system");
            }
            """
        );

        var output = result["user-id.ts"];
        await Assert.That(output).Contains("export type UserId = string & { readonly __brand: \"UserId\" };");
        await Assert.That(output).Contains("export namespace UserId");
        await Assert.That(output).Contains("function create(value: string): UserId");
        await Assert.That(output).Contains("function system(): UserId");
        await Assert.That(output).DoesNotContain("class UserId");
        // String wrappers don't need toString
        await Assert.That(output).DoesNotContain("function toString");
    }

    [Test]
    public async Task InlineWrapper_NonStringPrimitive_IncludesToString()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, InlineWrapper]
            public readonly record struct Amount(int Value);
            """
        );

        var output = result["amount.ts"];
        await Assert.That(output).Contains("export type Amount = number & { readonly __brand: \"Amount\" };");
        await Assert.That(output).Contains("function create(value: number): Amount");
        await Assert.That(output).Contains("function toString(value: Amount): string");
    }

    [Test]
    public async Task InlineWrapper_NewCall_IsRewrittenToCreate()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, InlineWrapper]
            public readonly record struct UserId(string Value);

            [Transpile]
            public class UserFactory
            {
                public UserId From(string value) => new UserId(value);
            }
            """
        );

        var output = result["user-factory.ts"];
        await Assert.That(output).Contains("return UserId.create(value);");
        await Assert.That(output).Contains("import { UserId }");
    }

    [Test]
    public async Task InlineWrapper_IneligibleStruct_FallsBackToClass()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, InlineWrapper]
            public readonly record struct CompositeId(string Prefix, string Value);
            """
        );

        var output = result["composite-id.ts"];
        await Assert.That(output).Contains("class CompositeId");
        await Assert.That(output).DoesNotContain("export namespace CompositeId");
    }
}
