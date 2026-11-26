using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// <c>x as T</c> lowers to its definition, <c>x is T ? x : null</c>, through
/// the existing type-pattern test (#279), except where the target is read
/// through its runtime type descriptor (spec 011 FR-026, D29): an interface
/// or a primitive lowers to <c>asType(x, descriptor)</c>. A <c>[PlainObject]</c>
/// tests its shape through its synthesized guard (FR-020); an array tests as
/// an array, with MS0046 (FR-021). A type with no runtime test in the target
/// reports MS0001, for <c>as</c> and <c>is</c> alike.
/// </summary>
public class AsCastTranspileTests
{
    private const string Types = """
        [Transpile] public class Person { public string Name { get; set; } = ""; }
        [Transpile] public interface IShape { double Area(); }
        [Transpile, GenerateGuard] public interface INamed { string Name { get; } }
        [Transpile, PlainObject] public record Dto(string Title);
        [Transpile, PlainObject, GenerateGuard] public record GuardedDto(string Name);
        [Transpile, Branded] public readonly record struct UserId(string Value);
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
    public async Task AsInterfaceWithGuard_TestsNominally()
    {
        var output = TranspileCasts("public static INamed? Of(object o) => o as INamed;");

        await Assert.That(output).Contains("return asType(o, INamed$runtimeType);");

        await Assert
            .That(output)
            .Contains("import { INamed$runtimeType, type INamed } from \"./i-named.js\";");
    }

    [Test]
    public async Task AsString_LowersToAsTypeWithTheStringDescriptor()
    {
        var output = TranspileCasts("public static string? Of(object o) => o as string;");

        await Assert.That(output).Contains("return asType(o, StringType);");
    }

    [Test]
    public async Task AsNullableValueType_TestsUnderlyingType()
    {
        var output = TranspileCasts("public static int? Of(object o) => o as int?;");

        await Assert.That(output).Contains("return asType(o, Int32Type);");
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
                "const receiver$temp$0 = Casts.next();\n\n"
                    + "    return receiver$temp$0 instanceof Person ? receiver$temp$0 : null;"
            );
    }

    /// <summary>
    /// <c>asType</c> evaluates its argument once, so a non-stable operand
    /// is passed straight in, with no immediately invoked function (D4).
    /// </summary>
    [Test]
    public async Task AsInterfaceOnANonStableOperand_PassesItToAsType()
    {
        var output = TranspileCasts(
            """
            private static object Next() => new Person();
            public static IShape? Of() => Next() as IShape;
            """
        );

        await Assert.That(output).Contains("return asType(Casts.next(), IShape$runtimeType);");
        await Assert.That(output).DoesNotContain("receiver$temp$");
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
                "return asType(a, StringType) ?? (b instanceof Person ? b : null) ?? \"none\";"
            );
    }

    [Test]
    public async Task AsInterfaceWithoutGuard_TestsNominally()
    {
        var output = TranspileCasts("public static IShape? Of(object o) => o as IShape;");

        await Assert.That(output).Contains("return asType(o, IShape$runtimeType);");
    }

    [Test]
    public async Task AsTypeParameter_Reified_LowersToAsType()
    {
        var output = TranspileCasts("public static T? Of<T>(object o) where T : class => o as T;");

        await Assert.That(output).Contains("return asType(o, T$type);");
    }

    [Test]
    public async Task AsNullableTypeParameter_Reified_LowersToAsType()
    {
        var output = TranspileCasts(
            "public static T? Of<T>(object o) where T : struct => o as T?;"
        );

        await Assert.That(output).Contains("return asType(o, T$type);");
    }

    [Test]
    public async Task AsPlainObjectWithoutGenerateGuard_CallsSynthesizedGuard()
    {
        var output = TranspileCasts("public static Dto? Of(object o) => o as Dto;");

        await Assert.That(output).Contains("return isDto(o) ? o : null;");
        await Assert.That(output).Contains("import { isDto, type Dto } from \"./dto.js\";");
        await Assert.That(output).DoesNotContain("instanceof Dto");
    }

    [Test]
    public async Task AsPlainObjectWithGuard_CallsGeneratedGuard()
    {
        var output = TranspileCasts("public static GuardedDto? Of(object o) => o as GuardedDto;");

        await Assert.That(output).Contains("return isGuardedDto(o) ? o : null;");

        await Assert
            .That(output)
            .Contains("import { isGuardedDto, type GuardedDto } from \"./guarded-dto.js\";");
    }

    [Test]
    public async Task AsBranded_TestsUnderlyingPrimitive()
    {
        var output = TranspileCasts("public static UserId? Of(object o) => o as UserId?;");

        await Assert.That(output).Contains("return isString(o) ? o : null;");
    }

    [Test]
    public async Task AsArray_TestsTheArrayAndWarnsErasedElement()
    {
        var (output, diagnostics) = TranspileCastsWithDiagnostics(
            "public static Person[]? Of(object o) => o as Person[];"
        );

        await Assert.That(output).Contains("return Array.isArray(o) ? o : null;");

        var erased = diagnostics.Single();

        await Assert.That(erased.Code).IsEqualTo(DiagnosticCodes.ErasedTypeArgumentTest);
        await Assert.That(erased.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(erased.Message).Contains("'o as Person[]'");
    }

    [Test]
    public async Task AsList_TestsThroughTheCollectionDescriptor()
    {
        var (output, diagnostics) = TranspileCastsWithDiagnostics(
            """
            public static System.Collections.Generic.List<string>? Of(object o) =>
                o as System.Collections.Generic.List<string>;
            """
        );

        await Assert.That(output).Contains("isType(o, listType(StringType)) ? o : null");
        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task IsPlainObjectWithoutGenerateGuard_CallsSynthesizedGuard()
    {
        var output = TranspileCasts("public static bool Of(object o) => o is Dto;");

        await Assert.That(output).Contains("return isDto(o);");
        await Assert.That(output).DoesNotContain("instanceof Dto");
    }

    [Test]
    public async Task IsUnguardedInterfacePattern_TestsNominally()
    {
        var output = TranspileCasts(
            "public static int Of(object o) => o switch { IShape => 1, _ => 0 };"
        );

        await Assert.That(output).Contains("isType(o, IShape$runtimeType)");
        await Assert.That(output).DoesNotContain("instanceof IShape");
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
