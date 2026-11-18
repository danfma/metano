namespace Metano.Tests;

/// <summary>
/// Spec 011 FR-024 (stage 011b B2): the <c>System.Enum</c> helpers read the
/// enum's runtime type descriptor through declarative mappings.
/// <c>Enum.IsDefined</c> lowers to <c>enumIsDefined</c>; <c>Enum.Parse</c>
/// (with an optional <c>ignoreCase</c>) and <c>Enum.ToObject</c> lower to
/// <c>enumFromRaw</c>. The generic forms read the type argument's descriptor
/// (<c>$R0</c>), which reaches it as <c>typeof</c> does.
/// </summary>
public class RuntimeTypeEnumHelperTests
{
    private const string Source = """
        using System;
        using Metano.Annotations;

        [assembly: TranspileAssembly]

        namespace Palette;

        [Flags]
        public enum Color
        {
            None = 0,
            Red = 1,
            Green = 2,
        }

        public enum Size
        {
            Small,
            Large,
        }

        public static class Probe
        {
            public static bool IsDefinedOf(object v) => Enum.IsDefined(typeof(Color), v);

            public static bool IsDefinedGeneric(Color c) => Enum.IsDefined(c);

            public static Color ParseGeneric(string s) => Enum.Parse<Color>(s);

            public static Color ParseIgnoringCase(string s) => Enum.Parse<Color>(s, true);

            public static object ParseOf(string s) => Enum.Parse(typeof(Size), s);

            public static object ParseOfIgnoringCase(string s) => Enum.Parse(typeof(Size), s, true);

            public static object FromRaw(int raw) => Enum.ToObject(typeof(Color), raw);
        }
        """;

    [Test]
    public async Task TheSource_ReportsNoDiagnostic()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Source);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task IsDefined_LowersToEnumIsDefined()
    {
        var probe = TranspileHelper.Transpile(Source)["palette/probe.ts"];

        await Assert.That(probe).Contains("return enumIsDefined(Color$runtimeType, v);");
        await Assert.That(probe).Contains("return enumIsDefined(Color$runtimeType, c);");
    }

    [Test]
    public async Task Parse_LowersToEnumFromRaw()
    {
        var probe = TranspileHelper.Transpile(Source)["palette/probe.ts"];

        await Assert.That(probe).Contains("return enumFromRaw(Color$runtimeType, s);");
        await Assert.That(probe).Contains("return enumFromRaw(Color$runtimeType, s, true);");
        await Assert.That(probe).Contains("return enumFromRaw(Size$runtimeType, s);");
        await Assert.That(probe).Contains("return enumFromRaw(Size$runtimeType, s, true);");
    }

    [Test]
    public async Task ToObject_LowersToEnumFromRaw()
    {
        var probe = TranspileHelper.Transpile(Source)["palette/probe.ts"];

        await Assert.That(probe).Contains("return enumFromRaw(Color$runtimeType, raw);");
    }

    [Test]
    public async Task Helpers_ReachTheEnumDescriptors()
    {
        var files = TranspileHelper.Transpile(Source);

        await Assert
            .That(files["palette/color.ts"])
            .Contains("export const Color$runtimeType: EnumType<Color> = {");

        await Assert
            .That(files["palette/size.ts"])
            .Contains("export const Size$runtimeType: EnumType<Size> = {");

        await Assert
            .That(files["palette/probe.ts"])
            .Contains("import { enumFromRaw, enumIsDefined } from \"metano-runtime\";");
    }
}
