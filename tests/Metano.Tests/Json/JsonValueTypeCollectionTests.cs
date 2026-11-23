namespace Metano.Tests.Json;

/// <summary>
/// <c>ImmutableArray&lt;T&gt;</c> is the one value-type collection: System.Text.Json
/// rejects JSON <c>null</c> for it, naming the CLR type (measured on .NET 10,
/// #713), so its descriptor names <c>typeName</c>; a reference-type
/// collection reads <c>null</c> and names none.
/// </summary>
public class JsonValueTypeCollectionTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        namespace TestApp;

        [Transpile]
        public sealed record Holder(ImmutableArray<int> Items, List<int> Others);

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
    public async Task ImmutableArray_NamesItsClrTypeName_ListDoesNot()
    {
        var context = TranspileHelper.Transpile(Source)["test-app/json-context.ts"];

        await Assert
            .That(context)
            .Contains(
                """typeName: "System.Collections.Immutable.ImmutableArray`1[System.Int32]","""
            );

        await Assert.That(context.Split("typeName:").Length).IsEqualTo(2);
    }
}
