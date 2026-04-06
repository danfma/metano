namespace MetaSharp.Tests;

public class InlineWrapperTranspileTests
{
    [Test]
    public async Task InlineWrapper_GeneratesBrandedTypeAndCompanion()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, InlineWrapper]
            public readonly struct UserId
            {
                public string Value { get; }
                public UserId(string value) { Value = value; }
                public static UserId System() => new("system");
            }
            """
        );

        var output = result["UserId.ts"];
        await Assert.That(output).Contains("export type UserId = string & { readonly __brand: \"UserId\" };");
        await Assert.That(output).Contains("export const UserId =");
        await Assert.That(output).Contains("create: (value: string) => value as UserId");
        await Assert.That(output).Contains("system: () => UserId.create(\"system\")");
        await Assert.That(output).DoesNotContain("class UserId");
    }

    [Test]
    public async Task InlineWrapper_NewCall_IsRewrittenToCreate()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, InlineWrapper]
            public readonly struct UserId
            {
                public string Value { get; }
                public UserId(string value) { Value = value; }
            }

            [Transpile]
            public class UserFactory
            {
                public UserId From(string value) => new UserId(value);
            }
            """
        );

        var output = result["UserFactory.ts"];
        await Assert.That(output).Contains("return UserId.create(value);");
        await Assert.That(output).Contains("import { UserId }");
    }

    [Test]
    public async Task InlineWrapper_IneligibleStruct_FallsBackToClass()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, InlineWrapper]
            public readonly struct CompositeId
            {
                public string Prefix { get; }
                public string Value { get; }
                public CompositeId(string prefix, string value)
                {
                    Prefix = prefix;
                    Value = value;
                }
            }
            """
        );

        var output = result["CompositeId.ts"];
        await Assert.That(output).Contains("class CompositeId");
        await Assert.That(output).DoesNotContain("export const CompositeId =");
    }
}
