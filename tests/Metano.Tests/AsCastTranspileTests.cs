using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// <c>x as T</c> lowers to its definition, <c>x is T ? x : null</c>, through
/// the existing type-pattern test (#279).
/// </summary>
public class AsCastTranspileTests
{
    private const string Types = """
        [Transpile] public class Person { public string Name { get; set; } = ""; }
        [Transpile] public interface IShape { double Area(); }
        [Transpile, GenerateGuard] public interface INamed { string Name { get; } }
        """;

    [Test]
    public async Task AsClass_LowersToInstanceofConditional()
    {
        var output = TranspileCasts("public static Person? Of(object o) => o as Person;");

        await Assert.That(output).Contains("return o instanceof Person ? o : null;");
    }

    [Test]
    public async Task AsClassIntoAnnotatedLocal_LowersToInstanceofConditional()
    {
        var output = TranspileCasts(
            """
            public static Person? Of(object o)
            {
                Person? person = o as Person;
                return person;
            }
            """
        );

        await Assert.That(output).Contains("const person = o instanceof Person ? o : null;");
    }

    [Test]
    public async Task AsInterfaceWithGuard_CallsGeneratedGuard()
    {
        var output = TranspileCasts("public static INamed? Of(object o) => o as INamed;");

        await Assert.That(output).Contains("return isINamed(o) ? o : null;");
        await Assert.That(output).Contains("import { isINamed, type INamed } from \"./i-named\";");
    }

    [Test]
    public async Task AsString_LowersToTypeofTest()
    {
        var output = TranspileCasts("public static string? Of(object o) => o as string;");

        await Assert.That(output).Contains("return typeof o === \"string\" ? o : null;");
    }

    [Test]
    public async Task AsNullableValueType_TestsUnderlyingType()
    {
        var output = TranspileCasts("public static int? Of(object o) => o as int?;");

        await Assert.That(output).Contains("return typeof o === \"number\" ? o : null;");
    }

    [Test]
    public async Task AsNonStableOperand_EvaluatesOperandOnce()
    {
        var output = TranspileCasts(
            """
            private static object Next() => new Person();
            public static Person? Of() => Next() as Person;
            """
        );

        await Assert
            .That(output)
            .Contains(
                "return ((receiver$temp$0) => receiver$temp$0 instanceof Person "
                    + "? receiver$temp$0 : null)(Casts.next());"
            );
    }

    [Test]
    public async Task AsInsideConditionAndCoalesceChain_KeepsGrouping()
    {
        var output = TranspileCasts(
            """
            public static bool IsPerson(object o) => (o as Person) != null;
            public static string NameOr(object o) => (o as Person)?.Name ?? "none";
            public static object First(object a, object b) =>
                a as string ?? b as Person ?? (object)"none";
            """
        );

        await Assert.That(output).Contains("return (o instanceof Person ? o : null) != null;");
        await Assert
            .That(output)
            .Contains("return (o instanceof Person ? o : null)?.name ?? \"none\";");
        await Assert
            .That(output)
            .Contains(
                "return (typeof a === \"string\" ? a : null) "
                    + "?? (b instanceof Person ? b : null) ?? \"none\";"
            );
    }

    [Test]
    public async Task AsInterfaceWithoutGuard_ReportsUnsupported()
    {
        var (output, diagnostics) = TranspileCastsWithDiagnostics(
            "public static IShape? Of(object o) => o as IShape;"
        );

        await AssertSingleUnsupported(diagnostics, "o as IShape");
        await Assert
            .That(output)
            .Contains("throw new Error(\"Unsupported expression: AsExpression\");");
    }

    [Test]
    public async Task AsTypeParameter_ReportsUnsupported()
    {
        var (_, diagnostics) = TranspileCastsWithDiagnostics(
            "public static T? Of<T>(object o) where T : class => o as T;"
        );

        await AssertSingleUnsupported(diagnostics, "o as T");
    }

    [Test]
    public async Task AsNullableTypeParameter_ReportsUnsupported()
    {
        var (_, diagnostics) = TranspileCastsWithDiagnostics(
            "public static T? Of<T>(object o) where T : struct => o as T?;"
        );

        await AssertSingleUnsupported(diagnostics, "o as T?");
    }

    private static string TranspileCasts(string members)
    {
        var (output, diagnostics) = TranspileCastsWithDiagnostics(members);

        if (diagnostics.Count > 0)
            throw new InvalidOperationException(
                "Unexpected diagnostics:\n" + string.Join("\n", diagnostics)
            );

        return output;
    }

    private static (
        string Output,
        IReadOnlyList<MetanoDiagnostic> Diagnostics
    ) TranspileCastsWithDiagnostics(string members)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            #nullable enable
            {{Types}}
            [Transpile]
            public static class Casts
            {
                {{members}}
            }
            """
        );

        return (files["casts.ts"], diagnostics);
    }

    private static async Task AssertSingleUnsupported(
        IReadOnlyList<MetanoDiagnostic> diagnostics,
        string expression
    )
    {
        var unsupported = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .ToList();

        await Assert.That(unsupported).HasSingleItem();
        await Assert.That(unsupported[0].Message).Contains($"'{expression}'");
        await Assert.That(unsupported[0].Location).IsNotNull();
    }
}
