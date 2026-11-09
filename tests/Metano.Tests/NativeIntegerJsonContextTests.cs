using Metano.Compiler.TypeScript;

namespace Metano.Tests;

/// <summary>
/// The JSON context of native integers (spec 012 DECIDED-2, issue #529): in
/// the staging <see cref="Int64Representation.BigInt"/> mode <c>nint</c>
/// follows <c>long</c> and <c>nuint</c> follows <c>ulong</c>, so every
/// native-integer property, element, dictionary key / value, root and
/// <c>[Branded]</c> / <c>[InlineWrapper]</c> wrapper emits the <c>int64</c>
/// descriptor naming <c>System.Int64</c> / <c>System.UInt64</c>.
/// System.Text.Json has no converter for <c>IntPtr</c> / <c>UIntPtr</c>
/// (measured on .NET 10 and 11: <c>NotSupportedException</c>), so the
/// descriptors, not a CLR error text, are the contract. A value root's
/// <c>typeName</c> (the type a JSON <c>null</c> error names) stays the CLR
/// name, <c>System.IntPtr</c>.
/// </summary>
public class NativeIntegerJsonContextTests
{
    private const string ContextPath = "test-app/json-context.ts";

    private const string Golden = "native-int-json-context.ts";

    private const string Source = """
        using System.Collections.Generic;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        namespace TestApp;

        [Transpile]
        public sealed record Handles(
            nint Signed,
            nuint Unsigned,
            nint? Parent,
            nuint[] Values,
            Dictionary<nint, string> ByHandle,
            Dictionary<string, nuint> Counts,
            NativeId Id,
            Dictionary<NativeId, string> ByToken,
            NativeQuota Quota
        );

        [Transpile, Branded]
        public readonly record struct NativeId(nint Value);

        [Transpile, InlineWrapper]
        public readonly record struct NativeQuota(nuint Value);

        [Transpile]
        [JsonSerializable(typeof(Handles))]
        [JsonSerializable(typeof(NativeId))]
        [JsonSerializable(typeof(NativeQuota))]
        [JsonSerializable(typeof(nint))]
        [JsonSerializable(typeof(nuint?))]
        public partial class JsonContext : JsonSerializerContext
        {
            public JsonContext() : base(new JsonSerializerOptions()) { }
            protected override JsonSerializerOptions? GeneratedSerializerOptions => null;
            public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
        }
        """;

    [Test]
    public async Task BigIntMode_EmitsTheInt64DescriptorsForNativeIntegers()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Source);

        if (TranspileHelper.UpdateGoldensRequested)
        {
            TranspileHelper.WriteExpected(Golden, files[ContextPath]);

            return;
        }

        await Assert.That(files[ContextPath]).IsEqualTo(TranspileHelper.ReadExpected(Golden));

        await Assert.That(diagnostics).IsEmpty();
    }
}
