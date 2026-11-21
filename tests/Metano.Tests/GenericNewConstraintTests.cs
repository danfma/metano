namespace Metano.Tests;

public class GenericNewConstraintTests
{
    [Test]
    public async Task NewOnTypeParameter_ReifiableInTypeScript_RetiresMs0019()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App;

            [Transpile]
            public static class Factory
            {
                public static T Make<T>() where T : new() => new T();
            }
            """
        );

        var ms0019 = diagnostics.FirstOrDefault(d =>
            d.Code == Metano.Compiler.Diagnostics.DiagnosticCodes.GenericNewConstraint
        );

        await Assert.That(ms0019).IsNull();
        await Assert.That(files["app/factory.ts"]).Contains("createInstance(T$type)");
    }

    [Test]
    public async Task NewOnTypeParameter_InDart_EmitsMs0019()
    {
        var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
            """
            using System;
            using Metano.Annotations;

            namespace App;

            [Transpile]
            public static class Factory
            {
                public static T Make<T>() where T : new() => new T();
            }
            """,
            new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
                Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview
            )
        );

        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "DartMs0019Test",
            [syntaxTree],
            TranspileHelper.BaseReferences,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary
            )
        );

        var ir = new Metano.Compiler.CSharpSourceFrontend().ExtractFromCompilation(
            compilation,
            Metano.Annotations.TargetLanguage.Dart
        );

        var ms0019 = ir.Diagnostics.FirstOrDefault(d =>
            d.Code == Metano.Compiler.Diagnostics.DiagnosticCodes.GenericNewConstraint
        );

        await Assert.That(ms0019).IsNotNull();
        await Assert.That(ms0019!.Message).Contains("new T()");

        await Assert
            .That(ms0019.Severity)
            .IsEqualTo(Metano.Compiler.Diagnostics.MetanoDiagnosticSeverity.Error);
    }

    [Test]
    public async Task NewOnConcreteType_DoesNotEmitMs0019()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App;

            [Transpile]
            public class Box { }

            [Transpile]
            public static class Factory
            {
                public static Box Make() => new Box();
            }
            """
        );

        var ms0019 = diagnostics.FirstOrDefault(d =>
            d.Code == Metano.Compiler.Diagnostics.DiagnosticCodes.GenericNewConstraint
        );

        await Assert.That(ms0019).IsNull();
    }
}
