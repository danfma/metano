using Metano.Compiler.Diagnostics;

namespace Metano.Tests.Json;

/// <summary>
/// A contract member typed as a non-polymorphic interface (#502): System.Text.Json
/// writes it through the interface's declared properties and throws
/// <c>NotSupportedException</c> only when reading a JSON object into it. The
/// closure claims a spec for the interface, as it does for an abstract class:
/// its wire properties, no <c>type:</c> (interfaces are type-only) and a
/// throwing factory. The golden is <c>json-interface-member-context.ts</c>.
/// </summary>
public class JsonInterfaceMemberTests
{
    private const string ContextPath = "test-app/json-context.ts";

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Text.Json.Serialization;

        namespace TestApp;

        [Transpile]
        public interface ISensor { int Level { get; } }

        [Transpile]
        public sealed record Probe(int Level) : ISensor;

        [Transpile]
        public sealed record Hub(string Title, ISensor? Sensor, List<ISensor> Sensors);

        [Transpile]
        [JsonSerializable(typeof(Hub))]
        public partial class JsonContext : JsonSerializerContext
        {
            public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
            protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
            public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
        }
        """;

    [Test]
    public async Task InterfaceMember_ClaimsAThrowingSpec_MatchesGolden()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Source);

        await Assert
            .That(diagnostics.Where(d => d.Severity == MetanoDiagnosticSeverity.Error))
            .IsEmpty();

        await TranspileHelper.AssertGolden("json-interface-member-context.ts", files[ContextPath]);
    }
}
