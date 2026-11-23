using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The JSON context of native integers (spec 012 DECIDED-2, issue #529): <c>nint</c>
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
    public async Task EmitsTheInt64DescriptorsForNativeIntegers()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Source);

        await TranspileHelper.AssertGolden(Golden, files[ContextPath]);

        await Assert
            .That(
                diagnostics.Where(diagnostic =>
                    diagnostic.Code != DiagnosticCodes.UnmirroredJsonOption
                )
            )
            .IsEmpty();
    }

    /// <summary>
    /// Each native-integer member and value root warns <c>MS0037</c> (#561):
    /// System.Text.Json throws for them, so the TypeScript superset can send
    /// a payload the .NET side cannot read. A <c>[Branded]</c> /
    /// <c>[InlineWrapper]</c> wrapper keeps its own documented wire shape
    /// (FR-026) and does not warn.
    /// </summary>
    [Test]
    public async Task WarnsForEachNativeIntegerMemberAndRoot()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Source);

        var messages = diagnostics
            .Where(diagnostic => diagnostic.Code == DiagnosticCodes.UnmirroredJsonOption)
            .Select(diagnostic => diagnostic.Message)
            .ToList();

        await Assert.That(messages.Count).IsEqualTo(8);

        foreach (
            var member in new[] { "Signed", "Unsigned", "Parent", "Values", "ByHandle", "Counts" }
        )
            await Assert
                .That(messages)
                .Contains(message =>
                    message.StartsWith($"'Handles.{member}' holds a nint or nuint")
                );

        await Assert
            .That(messages)
            .Contains(message => message.StartsWith("The [JsonSerializable] root 'nint'"));

        await Assert
            .That(messages)
            .Contains(message => message.StartsWith("The [JsonSerializable] root 'nuint?'"));
    }
}
