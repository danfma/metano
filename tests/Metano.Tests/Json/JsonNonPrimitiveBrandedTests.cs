namespace Metano.Tests.Json;

/// <summary>
/// A <c>[Branded]</c> wrapper over a non-primitive lowers as the record it is
/// (#288), so its JSON descriptor is a record spec with the .NET property
/// names (<c>Value</c>, <c>X</c>), constructed with <c>new</c>, never the
/// branded <c>{ kind: "branded", create: Boxed.create }</c> that pointed at a
/// factory the class does not declare.
/// </summary>
public class JsonNonPrimitiveBrandedTests
{
    private const string Source = """
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;
        using Metano.Annotations;

        namespace TestApp;

        [Transpile]
        public sealed record Inner(int X);

        [Transpile, Branded]
        public readonly record struct Boxed(Inner Value);

        [Transpile]
        public sealed record Holder(Boxed B);

        [Transpile]
        [JsonSerializable(typeof(Holder))]
        public partial class JsonContext : JsonSerializerContext
        {
            public JsonContext() : base(new JsonSerializerOptions()) { }
            protected override JsonSerializerOptions? GeneratedSerializerOptions => null;
            public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
        }
        """;

    [Test]
    [Arguments("factory: (p: Record<string, unknown>) => new Boxed(p.value as Inner),")]
    [Arguments("json: \"Value\",")]
    [Arguments("json: \"X\",")]
    [Arguments("spec: () => this.boxed,")]
    public async Task NonPrimitiveWrapper_IsARecordSpec(string expected)
    {
        var context = TranspileHelper.Transpile(Source)["test-app/json-context.ts"];

        await Assert.That(context).Contains(expected);
    }

    [Test]
    public async Task NonPrimitiveWrapper_HasNoBrandedDescriptor()
    {
        var context = TranspileHelper.Transpile(Source)["test-app/json-context.ts"];

        await Assert.That(context).DoesNotContain("Boxed.create");
        await Assert.That(context).DoesNotContain("kind: \"branded\"");
    }
}
