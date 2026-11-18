using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// One test per reason of the spec 011 stage 011b B1 and B2 diagnostics
/// (contracts/diagnostics.md): code, severity and location; a warning also
/// asserts that the output is emitted.
/// </summary>
public class RuntimeTypeDiagnosticsTests
{
    [Test]
    public async Task Ms0045_Reason1_TestAgainstADelegate()
    {
        var result = Transpile("public static bool Of(object o) => o is Action;");

        await AssertSingle(result, DiagnosticCodes.UnanswerableRuntimeTypeTest, "o is Action");

        await Assert
            .That(Single(result.Diagnostics, DiagnosticCodes.UnanswerableRuntimeTypeTest).Severity)
            .IsEqualTo(MetanoDiagnosticSeverity.Error);
    }

    [Test]
    public async Task Ms0048_Reason1_GetTypeOnAnObjectWhenAPlainObjectExists()
    {
        var result = Transpile(
            "public static string Of(object o) => o.GetType().Name;",
            "[Transpile, PlainObject] public record Dto(string Name);"
        );

        await AssertSingle(result, DiagnosticCodes.RuntimeTypeOfPlainObject, "o.GetType()");

        await Assert
            .That(Single(result.Diagnostics, DiagnosticCodes.RuntimeTypeOfPlainObject).Severity)
            .IsEqualTo(MetanoDiagnosticSeverity.Warning);

        await Assert.That(result.Output).Contains("return getRuntimeType(o).name;");
    }

    [Test]
    public async Task Ms0048_IsNotReportedWithoutAPlainObject()
    {
        var result = Transpile("public static string Of(object o) => o.GetType().Name;");

        await Assert
            .That(result.Diagnostics.Where(d => d.Code == DiagnosticCodes.RuntimeTypeOfPlainObject))
            .IsEmpty();
    }

    [Test]
    public async Task Ms0049_Reason1_ATypeMemberOutsideTheSurface()
    {
        var result = Transpile("public static bool Of(Type t) => t.GetMethod(\"Run\") != null;");

        await AssertSingle(result, DiagnosticCodes.UnsupportedReflection, "t.GetMethod(\"Run\")");
    }

    [Test]
    public async Task Ms0049_Reason2_AnOpenGenericDefinition()
    {
        var result = Transpile(
            "public static Type Of() => typeof(System.Collections.Generic.List<>);"
        );

        await AssertSingle(
            result,
            DiagnosticCodes.UnsupportedReflection,
            "typeof(System.Collections.Generic.List<>)"
        );
    }

    [Test]
    public async Task Ms0049_Reason2_TypeOfVoid()
    {
        var result = Transpile("public static Type Of() => typeof(void);");

        await AssertSingle(result, DiagnosticCodes.UnsupportedReflection, "typeof(void)");
    }

    [Test]
    public async Task Ms0049_Reason2_ActivatorCreateInstance()
    {
        var result = Transpile("public static object? Of(Type t) => Activator.CreateInstance(t);");

        await AssertSingle(
            result,
            DiagnosticCodes.UnsupportedReflection,
            "Activator.CreateInstance(t)"
        );
    }

    [Test]
    public async Task Ms0049_Reason2_TypeGetTypeOfAName()
    {
        var result = Transpile("public static Type? Of() => Type.GetType(\"Shop.Order\");");

        await AssertSingle(
            result,
            DiagnosticCodes.UnsupportedReflection,
            "Type.GetType(\"Shop.Order\")"
        );
    }

    [Test]
    public async Task Ms0049_Reason3_GetTypeOnANullLiteral()
    {
        var result = Transpile("public static Type Of() => ((object)null!).GetType();");

        await AssertSingle(
            result,
            DiagnosticCodes.UnsupportedReflection,
            "((object)null!).GetType()"
        );
    }

    [Test]
    public async Task Ms0055_Reason1_ACastToAnArrayOfAnErasedElement()
    {
        var result = Transpile("public static int[] Of(object o) => (int[])o;");

        await AssertUncheckedCast(result, "(int[])o");
        await Assert.That(result.Output).Contains("return o as number[];");
    }

    [Test]
    public async Task Ms0055_IsNotReportedForAnArrayOfObjects()
    {
        var result = Transpile("public static object[] Of(object o) => (object[])o;");

        await Assert
            .That(result.Diagnostics.Where(d => d.Code == DiagnosticCodes.UncheckedCast))
            .IsEmpty();
    }

    [Test]
    public async Task Ms0055_Reason2_ACastToADelegate()
    {
        var result = Transpile("public static Action Of(object o) => (Action)o;");

        await AssertUncheckedCast(result, "(Action)o");
        await Assert.That(result.Output).Contains("return o as () => void;");
    }

    [Test]
    public async Task Ms0055_Reason3_ACastToATypeParameter()
    {
        var result = Transpile("public static T Of<T>(object o) => (T)o;");

        await AssertUncheckedCast(result, "(T)o");
        await Assert.That(result.Output).Contains("return o as T;");
    }

    [Test]
    public async Task Ms0045_Reason3_APlainObjectWithNoGuard()
    {
        var result = Transpile(
            "public static bool Of(object o) => o is Oops;",
            "[Transpile, PlainObject] public class Oops : Exception { }"
        );

        await AssertSingle(result, DiagnosticCodes.UnanswerableRuntimeTypeTest, "o is Oops");

        await Assert
            .That(Single(result.Diagnostics, DiagnosticCodes.UnanswerableRuntimeTypeTest).Severity)
            .IsEqualTo(MetanoDiagnosticSeverity.Error);
    }

    [Test]
    public async Task Ms0046_Reason1_ATestOfAnArrayOfAnErasedElement()
    {
        var result = Transpile("public static bool Of(object o) => o is int[];");

        await AssertSingle(result, DiagnosticCodes.ErasedTypeArgumentTest, "o is int[]");

        await Assert
            .That(Single(result.Diagnostics, DiagnosticCodes.ErasedTypeArgumentTest).Severity)
            .IsEqualTo(MetanoDiagnosticSeverity.Warning);

        await Assert.That(result.Output).Contains("return Array.isArray(o);");
    }

    [Test]
    public async Task Ms0046_IsNotReportedForAnArrayOfObjects()
    {
        var result = Transpile("public static bool Of(object o) => o is object[];");

        await Assert
            .That(result.Diagnostics.Where(d => d.Code == DiagnosticCodes.ErasedTypeArgumentTest))
            .IsEmpty();
    }

    [Test]
    public async Task Ms0047_AStructuralTestAnotherPlainObjectPasses()
    {
        var result = Transpile(
            """
            public static bool Of(object o) => o is Named;

            public static bool Again(object o) => o is Named;
            """,
            """
            [Transpile, PlainObject] public record Named(string Label);
            [Transpile, PlainObject] public record Tagged(string Label, int Count);
            """
        );

        await AssertSingle(result, DiagnosticCodes.PlainObjectShapeOverlap, "o is Named");

        var overlap = Single(result.Diagnostics, DiagnosticCodes.PlainObjectShapeOverlap);

        await Assert.That(overlap.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(overlap.Message).Contains("'Tagged' also satisfies the shape { label }");
        await Assert.That(result.Output).Contains("return isNamed(o);");
    }

    [Test]
    public async Task Ms0047_IsNotReportedWhenTheStaticTypeCannotHoldTheOther()
    {
        var result = Transpile(
            "public static bool Of(IHasLabel o) => o is Named;",
            """
            [Transpile] public interface IHasLabel { string Label { get; } }
            [Transpile, PlainObject] public record Named(string Label) : IHasLabel;
            [Transpile, PlainObject] public record Tagged(string Label, int Count);
            """
        );

        await Assert
            .That(result.Diagnostics.Where(d => d.Code == DiagnosticCodes.PlainObjectShapeOverlap))
            .IsEmpty();
    }

    [Test]
    public async Task Ms0048_Reason2_GetTypeOnAPlainObjectOthersDeriveFrom()
    {
        var result = Transpile(
            "public static string Of(Base b) => b.GetType().Name;",
            """
            [Transpile, PlainObject] public record Base(string Label);
            [Transpile, PlainObject] public record Derived(string Label, int Count) : Base(Label);
            """
        );

        await AssertSingle(result, DiagnosticCodes.RuntimeTypeOfPlainObject, "b.GetType()");

        await Assert
            .That(Single(result.Diagnostics, DiagnosticCodes.RuntimeTypeOfPlainObject).Severity)
            .IsEqualTo(MetanoDiagnosticSeverity.Warning);

        await Assert.That(result.Output).Contains("return getRuntimeType(b).name;");
    }

    private static async Task AssertUncheckedCast(Transpiled result, string cast)
    {
        await AssertSingle(result, DiagnosticCodes.UncheckedCast, cast);

        var diagnostic = Single(result.Diagnostics, DiagnosticCodes.UncheckedCast);

        await Assert.That(diagnostic.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);

        await Assert
            .That(diagnostic.Message)
            .EndsWith("Use x.UnsafeCast<T>() to state that the cast is intentionally unchecked.");
    }

    private static async Task AssertSingle(Transpiled result, string code, string site)
    {
        var matching = result.Diagnostics.Where(d => d.Code == code).ToList();

        await Assert.That(matching).HasSingleItem();
        await Assert.That(matching[0].Location).IsNotNull();
        await Assert.That(SourceTextAt(matching[0], result.Source)).IsEqualTo(site);
    }

    private static MetanoDiagnostic Single(
        IReadOnlyList<MetanoDiagnostic> diagnostics,
        string code
    ) => diagnostics.Single(d => d.Code == code);

    /// <summary>
    /// The source text a diagnostic points at: a phase diagnostic carries a
    /// file location built from its recorded span, so the span is read off the
    /// compiled source.
    /// </summary>
    private static string SourceTextAt(MetanoDiagnostic diagnostic, string source) =>
        source.Substring(
            diagnostic.Location!.SourceSpan.Start,
            diagnostic.Location.SourceSpan.Length
        );

    private static Transpiled Transpile(string members, string types = "")
    {
        var source = $$"""
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
            """;

        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(source);

        // TranspileHelper compiles the source behind its implicit usings.
        const string compiledPrefix =
            "using System;\nusing System.Threading.Tasks;\nusing Metano.Annotations;\n";

        return new Transpiled(files["probe/questions.ts"], diagnostics, compiledPrefix + source);
    }

    private sealed record Transpiled(
        string Output,
        IReadOnlyList<MetanoDiagnostic> Diagnostics,
        string Source
    );
}
