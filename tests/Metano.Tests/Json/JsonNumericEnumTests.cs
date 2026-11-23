namespace Metano.Tests.Json;

/// <summary>
/// The value descriptor of a numeric enum (#369): System.Text.Json reads any
/// integer of the enum's underlying type, declared or not, so a descriptor
/// names its <c>numberType</c> in every slot for any underlying type but
/// <c>int</c> (the runtime's default), and the runtime range-checks the read
/// by it.
/// </summary>
public class JsonNumericEnumTests
{
    private const string ContextPath = "test-app/json-context.ts";

    private const string Source = """
        using System;
        using System.Text.Json.Serialization;

        namespace TestApp;

        [Transpile]
        public enum Level { Low, High }

        [Transpile]
        public enum Shade : byte { Light, Dark }

        [Transpile, Flags]
        public enum Perms : uint { None = 0, Read = 1, Write = 2 }

        [Transpile]
        public sealed record Paint(Level Level, Shade Shade, Perms? Perms);

        [Transpile]
        [JsonSerializable(typeof(Paint))]
        public partial class JsonContext : JsonSerializerContext
        {
            public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }
            protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => null;
            public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type) => null;
        }
        """;

    [Test]
    public async Task IntEnum_NamesNoNumberType()
    {
        var context = TranspileHelper.Transpile(Source)[ContextPath];

        await Assert
            .That(context)
            .Contains(
                """type: { kind: "numericEnum", values: Level, typeName: "TestApp.Level" },"""
            );
    }

    [Test]
    public async Task NonIntEnum_NamesItsUnderlyingTypeInEverySlot()
    {
        var context = TranspileHelper.Transpile(Source)[ContextPath];

        await Assert.That(context).Contains("""numberType: "System.Byte",""");

        await Assert.That(context).Contains("""numberType: "System.UInt32",""");
    }
}
