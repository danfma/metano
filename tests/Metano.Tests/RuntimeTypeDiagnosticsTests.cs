using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// One test per reason of the spec 011 stage 011b B1 diagnostics
/// (contracts/diagnostics.md): code, severity and location; a warning also
/// asserts that the output is emitted.
/// </summary>
public class RuntimeTypeDiagnosticsTests
{
    [Test]
    public async Task Ms0045_Reason1_TestAgainstADelegate()
    {
        var (_, diagnostics) = Transpile("public static bool Of(object o) => o is Action;");

        await AssertSingle(diagnostics, DiagnosticCodes.UnanswerableRuntimeTypeTest, "o is Action");

        await Assert
            .That(Single(diagnostics, DiagnosticCodes.UnanswerableRuntimeTypeTest).Severity)
            .IsEqualTo(MetanoDiagnosticSeverity.Error);
    }

    [Test]
    public async Task Ms0048_Reason1_GetTypeOnAnObjectWhenAPlainObjectExists()
    {
        var (output, diagnostics) = Transpile(
            "public static string Of(object o) => o.GetType().Name;",
            "[Transpile, PlainObject] public record Dto(string Name);"
        );

        await AssertSingle(diagnostics, DiagnosticCodes.RuntimeTypeOfPlainObject, "o.GetType()");

        await Assert
            .That(Single(diagnostics, DiagnosticCodes.RuntimeTypeOfPlainObject).Severity)
            .IsEqualTo(MetanoDiagnosticSeverity.Warning);

        await Assert.That(output).Contains("return getRuntimeType(o).name;");
    }

    [Test]
    public async Task Ms0048_IsNotReportedWithoutAPlainObject()
    {
        var (_, diagnostics) = Transpile("public static string Of(object o) => o.GetType().Name;");

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.RuntimeTypeOfPlainObject))
            .IsEmpty();
    }

    [Test]
    public async Task Ms0049_Reason1_ATypeMemberOutsideTheSurface()
    {
        var (_, diagnostics) = Transpile(
            "public static bool Of(Type t) => t.GetMethod(\"Run\") != null;"
        );

        await AssertSingle(
            diagnostics,
            DiagnosticCodes.UnsupportedReflection,
            "t.GetMethod(\"Run\")"
        );
    }

    [Test]
    public async Task Ms0049_Reason2_AnOpenGenericDefinition()
    {
        var (_, diagnostics) = Transpile(
            "public static Type Of() => typeof(System.Collections.Generic.List<>);"
        );

        await AssertSingle(
            diagnostics,
            DiagnosticCodes.UnsupportedReflection,
            "typeof(System.Collections.Generic.List<>)"
        );
    }

    [Test]
    public async Task Ms0049_Reason2_TypeOfVoid()
    {
        var (_, diagnostics) = Transpile("public static Type Of() => typeof(void);");

        await AssertSingle(diagnostics, DiagnosticCodes.UnsupportedReflection, "typeof(void)");
    }

    [Test]
    public async Task Ms0049_Reason2_ActivatorCreateInstance()
    {
        var (_, diagnostics) = Transpile(
            "public static object? Of(Type t) => Activator.CreateInstance(t);"
        );

        await AssertSingle(
            diagnostics,
            DiagnosticCodes.UnsupportedReflection,
            "Activator.CreateInstance(t)"
        );
    }

    [Test]
    public async Task Ms0049_Reason2_TypeGetTypeOfAName()
    {
        var (_, diagnostics) = Transpile(
            "public static Type? Of() => Type.GetType(\"Shop.Order\");"
        );

        await AssertSingle(
            diagnostics,
            DiagnosticCodes.UnsupportedReflection,
            "Type.GetType(\"Shop.Order\")"
        );
    }

    [Test]
    public async Task Ms0049_Reason3_GetTypeOnANullLiteral()
    {
        var (_, diagnostics) = Transpile("public static Type Of() => ((object)null!).GetType();");

        await AssertSingle(
            diagnostics,
            DiagnosticCodes.UnsupportedReflection,
            "((object)null!).GetType()"
        );
    }

    [Test]
    public async Task Ms0055_Reason1_ACastToAnArrayOfAnErasedElement()
    {
        var (output, diagnostics) = Transpile("public static int[] Of(object o) => (int[])o;");

        await AssertUncheckedCast(diagnostics, "(int[])o");
        await Assert.That(output).Contains("return o as number[];");
    }

    [Test]
    public async Task Ms0055_IsNotReportedForAnArrayOfObjects()
    {
        var (_, diagnostics) = Transpile("public static object[] Of(object o) => (object[])o;");

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UncheckedCast))
            .IsEmpty();
    }

    [Test]
    public async Task Ms0055_Reason2_ACastToADelegate()
    {
        var (output, diagnostics) = Transpile("public static Action Of(object o) => (Action)o;");

        await AssertUncheckedCast(diagnostics, "(Action)o");
        await Assert.That(output).Contains("return o as () => void;");
    }

    [Test]
    public async Task Ms0055_Reason3_ACastToATypeParameter()
    {
        var (output, diagnostics) = Transpile("public static T Of<T>(object o) => (T)o;");

        await AssertUncheckedCast(diagnostics, "(T)o");
        await Assert.That(output).Contains("return o as T;");
    }

    private static async Task AssertUncheckedCast(
        IReadOnlyList<MetanoDiagnostic> diagnostics,
        string cast
    )
    {
        await AssertSingle(diagnostics, DiagnosticCodes.UncheckedCast, cast);

        var diagnostic = Single(diagnostics, DiagnosticCodes.UncheckedCast);

        await Assert.That(diagnostic.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);

        await Assert
            .That(diagnostic.Message)
            .EndsWith("Use x.UnsafeCast<T>() to state that the cast is intentionally unchecked.");
    }

    private static async Task AssertSingle(
        IReadOnlyList<MetanoDiagnostic> diagnostics,
        string code,
        string site
    )
    {
        var matching = diagnostics.Where(d => d.Code == code).ToList();

        await Assert.That(matching).HasSingleItem();
        await Assert.That(matching[0].Location).IsNotNull();
        await Assert.That(SourceTextAt(matching[0])).IsEqualTo(site);
    }

    private static MetanoDiagnostic Single(
        IReadOnlyList<MetanoDiagnostic> diagnostics,
        string code
    ) => diagnostics.Single(d => d.Code == code);

    private static string SourceTextAt(MetanoDiagnostic diagnostic) =>
        diagnostic.Location!.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static (string Output, IReadOnlyList<MetanoDiagnostic> Diagnostics) Transpile(
        string members,
        string types = ""
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            #nullable enable
            using System;
            using Metano.Annotations;

            namespace Probe;

            {{types}}

            [Transpile]
            public static class Questions
            {
                {{members}}
            }
            """
        );

        return (files["probe/questions.ts"], diagnostics);
    }
}
