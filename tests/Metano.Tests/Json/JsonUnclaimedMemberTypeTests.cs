using Metano.Compiler.Diagnostics;

namespace Metano.Tests.Json;

/// <summary>
/// A serialized member whose type the context cannot claim a spec for (#713):
/// <c>KeyValuePair&lt;K, V&gt;</c>, <c>object</c>, a type of a non-Metano
/// assembly. It used to emit a <c>ref</c> to a getter the context never
/// declares (tsgo TS2339, a runtime crash on the first use); it now reports
/// <c>MS0001</c> at the member. <c>[JsonIgnore]</c> leaves the member out.
/// </summary>
public class JsonUnclaimedMemberTypeTests
{
    [Test]
    public async Task KeyValuePairMember_ReportsMs0001()
    {
        var errors = ErrorsOf(
            "public sealed record Pairs(KeyValuePair<string, int> One, List<KeyValuePair<string, int>> Many);"
        );

        await Assert.That(errors).Count().IsEqualTo(2);

        await Assert
            .That(errors[0])
            .StartsWith(
                "'Pairs.One' cannot be serialized by the TypeScript JSON context: "
                    + "'KeyValuePair<string, int>' has no JSON contract in it"
            );
    }

    [Test]
    public async Task ObjectMember_ReportsMs0001()
    {
        var errors = ErrorsOf("public sealed record Bag(object Any);");

        await Assert.That(errors).Count().IsEqualTo(1);
    }

    [Test]
    public async Task IgnoredMember_ReportsNothing()
    {
        var errors = ErrorsOf(
            "public sealed class Bag { [JsonIgnore] public object? Any { get; set; } public int N { get; set; } }"
        );

        await Assert.That(errors).IsEmpty();
    }

    private static List<string> ErrorsOf(string declaration)
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Text.Json;
            using System.Text.Json.Serialization;

            namespace TestApp;

            [Transpile]
            {{declaration}}

            [Transpile]
            [JsonSerializable(typeof({{RootOf(declaration)}}))]
            public partial class JsonContext : JsonSerializerContext
            {
                public JsonContext() : base(new JsonSerializerOptions()) { }
                protected override JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
            }
            """;

        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(source);

        return diagnostics
            .Where(diagnostic => diagnostic.Code == DiagnosticCodes.UnsupportedFeature)
            .Select(diagnostic => diagnostic.Message)
            .ToList();
    }

    /// <summary>The declared type's name: the word after <c>record</c> / <c>class</c>.</summary>
    private static string RootOf(string declaration) => declaration.Split(' ', '(')[3];
}
