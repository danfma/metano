using Metano.Compiler;
using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Metano.Tests;

/// <summary>
/// The cases where a reference to an <c>[Ignore]</c> (.NET-only) type does NOT
/// raise <c>MS0013</c> (#247): source-generator output, an interface ignored for the
/// active target in a transpilable type's base list (erased from
/// <c>implements</c>/<c>extends</c>), and attribute applications the compiler does not
/// read for emission (<c>nameof</c> is covered by
/// <see cref="IgnoreReferencePositionTests"/>). Every other position keeps raising — see
/// <see cref="IgnoreDotNetOnlyTests"/> plus the negative cases below.
/// </summary>
public class IgnoreReferenceExemptionTests
{
    private const string JsonUsings = """
        using System.Text.Json;
        using System.Text.Json.Serialization;
        """;

    private const string WorkspaceTextContracts = """
        namespace Studio;

        [Ignore]
        public interface IWorkspaceText<TSelf>
            where TSelf : IWorkspaceText<TSelf>
        {
            static abstract TSelf From(string value);
        }

        [Ignore]
        public sealed class WorkspaceTextJsonConverter<T> : JsonConverter<T>
            where T : IWorkspaceText<T>
        {
            public override T Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options
            ) => T.From(reader.GetString()!);

            public override void Write(
                Utf8JsonWriter writer,
                T value,
                JsonSerializerOptions options
            ) => writer.WriteStringValue(value.ToString());
        }
        """;

    private const string AnnotatedWorkspaceText = """
        [Branded]
        [JsonConverter(typeof(WorkspaceTextJsonConverter<WorkspaceText>))]
        public readonly record struct WorkspaceText(string Value) : IWorkspaceText<WorkspaceText>
        {
            public static WorkspaceText From(string value) => new(value.Trim());
        }
        """;

    private const string PlainWorkspaceText = """
        [Branded]
        public readonly record struct WorkspaceText(string Value)
        {
            public static WorkspaceText From(string value) => new(value.Trim());
        }
        """;

    private const string DotNetOnlyNameInterface = """
        [Ignore]
        public interface IDotNetOnly
        {
            string Name { get; }
        }
        """;

    private const string UpperCaseConverter = """
        [Ignore]
        public sealed class UpperCaseConverter : JsonConverter<string>
        {
            public override string Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options
            ) => reader.GetString()!.ToUpperInvariant();

            public override void Write(
                Utf8JsonWriter writer,
                string value,
                JsonSerializerOptions options
            ) => writer.WriteStringValue(value);
        }
        """;

    [Test]
    public async Task BrandedValueObject_WithIgnoreInterfaceAndConverterAttribute_DoesNotRaiseMs0013()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            WorkspaceTextProgram(AnnotatedWorkspaceText)
        );

        await Assert.That(IgnoreReferenceDiagnostics(diagnostics)).IsEmpty();
        await Assert.That(files).ContainsKey("studio/workspace-text.ts");
    }

    [Test]
    public async Task BrandedValueObject_WithIgnoreInterfaceAndConverterAttribute_EmitsUnchangedTypeScript()
    {
        var annotated = TranspileHelper.Transpile(WorkspaceTextProgram(AnnotatedWorkspaceText));
        var plain = TranspileHelper.Transpile(WorkspaceTextProgram(PlainWorkspaceText));

        await Assert
            .That(annotated["studio/workspace-text.ts"])
            .IsEqualTo(plain["studio/workspace-text.ts"]);
    }

    [Test]
    public async Task IgnoreInterface_OnTranspilableClass_IsErasedFromImplements()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            namespace App;

            {{DotNetOnlyNameInterface}}

            [Transpile]
            public interface IShared
            {
                string Name { get; }
            }

            [Transpile]
            public class Person : IDotNetOnly, IShared
            {
                public string Name { get; } = "";
            }
            """
        );

        var ts = files["app/person.ts"];

        await Assert.That(IgnoreReferenceDiagnostics(diagnostics)).IsEmpty();
        await Assert.That(ts).Contains("export class Person implements IShared {");
        await Assert.That(ts).DoesNotContain("IDotNetOnly");
    }

    [Test]
    public async Task IgnoreInterface_AsOnlyBaseOfTranspilableClass_DropsImplementsClause()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            namespace App;

            {{DotNetOnlyNameInterface}}

            [Transpile]
            public class Person : IDotNetOnly
            {
                public string Name { get; } = "";
            }
            """
        );

        var ts = files["app/person.ts"];

        await Assert.That(IgnoreReferenceDiagnostics(diagnostics)).IsEmpty();
        await Assert.That(ts).Contains("export class Person {");
        await Assert.That(ts).DoesNotContain("IDotNetOnly");
    }

    [Test]
    public async Task IgnoreInterface_InTranspilableInterfaceBaseList_IsErasedFromExtends()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            namespace App;

            {{DotNetOnlyNameInterface}}

            [Transpile]
            public interface IPerson : IDotNetOnly
            {
                int Age { get; }
            }
            """
        );

        var ts = files["app/i-person.ts"];

        await Assert.That(IgnoreReferenceDiagnostics(diagnostics)).IsEmpty();
        await Assert.That(ts).Contains("export interface IPerson {");
        await Assert.That(ts).DoesNotContain("IDotNetOnly");
    }

    [Test]
    public async Task IgnoreInterface_WithKeptBaseInterface_ImplementsTheKeptBase()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App;

            [Transpile]
            public interface IKept
            {
                string Name { get; }
            }

            [Ignore]
            public interface IDotNetOnly : IKept {}

            [Ignore]
            public interface IDeeperDotNetOnly : IDotNetOnly {}

            [Transpile]
            public class Person : IDeeperDotNetOnly, IKept
            {
                public string Name { get; } = "";
            }

            [Transpile]
            public class Robot : IDeeperDotNetOnly
            {
                public string Name { get; } = "";
            }
            """
        );

        await Assert.That(IgnoreReferenceDiagnostics(diagnostics)).IsEmpty();

        await Assert
            .That(files["app/person.ts"])
            .Contains("export class Person implements IKept {");

        await Assert.That(files["app/robot.ts"]).Contains("export class Robot implements IKept {");
        await Assert.That(files["app/robot.ts"]).Contains("IKept }");
    }

    [Test]
    public async Task IgnoreInterface_ForDartOnly_StaysInTypeScriptImplements()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App;

            [Transpile, Ignore(TargetLanguage.Dart)]
            public interface ITypeScriptOnly
            {
                string Name { get; }
            }

            [Transpile]
            public class Person : ITypeScriptOnly
            {
                public string Name { get; } = "";
            }
            """
        );

        await Assert.That(IgnoreReferenceDiagnostics(diagnostics)).IsEmpty();

        await Assert
            .That(files["app/person.ts"])
            .Contains("export class Person implements ITypeScriptOnly {");
    }

    [Test]
    public async Task IgnoreInterface_OnTranspilableClass_IsErasedFromDartImplements()
    {
        var (files, _) = TranspileHelper.TranspileDart(
            $$"""
            using Metano.Annotations;

            {{DotNetOnlyNameInterface}}

            [Transpile]
            public interface IShared
            {
                string Name { get; }
            }

            [Transpile]
            public class Person : IDotNetOnly, IShared
            {
                public string Name { get; } = "";
            }
            """
        );

        var dart = files["person.dart"];

        await Assert.That(dart).Contains("class Person implements IShared {");
        await Assert.That(dart).DoesNotContain("IDotNetOnly");
    }

    [Test]
    public async Task IgnoreInterface_WithKeptBaseInterface_DartImplementsTheKeptBase()
    {
        var (files, _) = TranspileHelper.TranspileDart(
            """
            using Metano.Annotations;

            [Transpile]
            public interface IKept
            {
                string Name { get; }
            }

            [Ignore]
            public interface IDotNetOnly : IKept {}

            [Transpile]
            public class Person : IDotNetOnly
            {
                public string Name { get; } = "";
            }
            """
        );

        await Assert.That(files["person.dart"]).Contains("class Person implements IKept {");
    }

    [Test]
    public async Task IgnoreType_InTypeOfInsideMethodAttribute_DoesNotRaiseMs0013()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [assembly: TranspileAssembly]

            [Ignore]
            public class Marker {}

            [AttributeUsage(AttributeTargets.All)]
            [Ignore]
            public sealed class UsesTypeAttribute(Type type) : Attribute
            {
                public Type Type { get; } = type;
            }

            public class Consumer
            {
                [UsesType(typeof(Marker))]
                public int Run([UsesType(typeof(Marker))] int value) => value;
            }
            """
        );

        await Assert.That(IgnoreReferenceDiagnostics(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task IgnoreAttributeClass_AppliedToTranspilableCode_DoesNotRaiseMs0013()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [assembly: TranspileAssembly]

            [AttributeUsage(AttributeTargets.All)]
            [Ignore]
            public sealed class DotNetOnlyAttribute : Attribute {}

            [DotNetOnly]
            public record Consumer([property: DotNetOnly] int Value)
            {
                [DotNetOnly]
                public int Run([DotNetOnly] int value) => value;
            }
            """
        );

        await Assert.That(IgnoreReferenceDiagnostics(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task IgnoreType_InJsonSerializableAttribute_RaisesMs0013()
    {
        // [JsonSerializable(typeof(T))] is read by the JSON-context transformer,
        // which emits a type spec for T — so the attribute is not exempt.
        var (_, diagnostics) = TranspileHelper.TranspileWithJsonSourceGenerator(
            $$"""
            {{JsonUsings}}
            [assembly: TranspileAssembly]

            namespace Studio;

            [Ignore]
            public sealed class DotNetOnlyDocument
            {
                public string Title { get; set; } = "";
            }

            [JsonSerializable(typeof(DotNetOnlyDocument))]
            public partial class StudioJsonContext : JsonSerializerContext;
            """
        );

        var ms0013 = IgnoreReferenceDiagnostics(diagnostics);

        await Assert.That(ms0013).IsNotEmpty();
        await Assert.That(ms0013[0].Message).Contains("DotNetOnlyDocument");
    }

    [Test]
    public async Task IgnoreType_InTypeOfOutsideAttribute_RaisesMs0013()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [assembly: TranspileAssembly]

            [Ignore]
            public class Marker {}

            public class Consumer
            {
                public string Run() => typeof(Marker).Name;
            }
            """
        );

        await Assert.That(IgnoreReferenceDiagnostics(diagnostics)).IsNotEmpty();
    }

    [Test]
    public async Task IgnoreInterface_AsParameterType_RaisesMs0013()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [assembly: TranspileAssembly]

            [Ignore]
            public interface IDotNetOnly {}

            public class Consumer
            {
                public void Use(IDotNetOnly value) {}
            }
            """
        );

        await Assert.That(IgnoreReferenceDiagnostics(diagnostics)).IsNotEmpty();
    }

    [Test]
    public async Task IgnoreInterface_AsTypeArgumentOfBaseInterface_RaisesMs0013()
    {
        // Only the [Ignore] interface itself is erased from the base list —
        // a transpilable generic interface closed over an [Ignore] type
        // would still leak the type into `implements`.
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [assembly: TranspileAssembly]

            [Ignore]
            public class Marker {}

            public interface IHolder<T> {}

            public class Consumer : IHolder<Marker> {}
            """
        );

        await Assert.That(IgnoreReferenceDiagnostics(diagnostics)).IsNotEmpty();
    }

    [Test]
    public async Task IgnoreInterface_AsGenericConstraint_RaisesMs0013()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [assembly: TranspileAssembly]

            [Ignore]
            public interface IDotNetOnly {}

            public class Consumer
            {
                public void Use<T>(T value) where T : IDotNetOnly {}
            }
            """
        );

        await Assert.That(IgnoreReferenceDiagnostics(diagnostics)).IsNotEmpty();
    }

    [Test]
    public async Task SourceGeneratedJsonContext_ReferencingIgnoreConverter_DoesNotRaiseMs0013()
    {
        // The property-level [JsonConverter] is exempt (attribute), but the real
        // System.Text.Json generator emits `new UpperCaseConverter()` into the
        // context's generated partial — not user-authored, so not scanned.
        var (_, diagnostics) = TranspileHelper.TranspileWithJsonSourceGenerator(
            $$"""
            {{JsonUsings}}
            [assembly: TranspileAssembly]

            namespace Studio;

            {{UpperCaseConverter}}

            public sealed class Document
            {
                [JsonConverter(typeof(UpperCaseConverter))]
                public string Title { get; set; } = "";
            }

            [JsonSerializable(typeof(Document))]
            public partial class StudioJsonContext : JsonSerializerContext;
            """
        );

        await Assert.That(IgnoreReferenceDiagnostics(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task SourceGeneratedJsonContext_WithBrandedWorkspaceText_DoesNotRaiseMs0013()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithJsonSourceGenerator(
            WorkspaceTextProgram(
                $"""
                {AnnotatedWorkspaceText}

                public sealed record Document(WorkspaceText Title);

                [JsonSerializable(typeof(Document))]
                public partial class StudioJsonContext : JsonSerializerContext;
                """
            )
        );

        await Assert.That(IgnoreReferenceDiagnostics(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task UserFileNamedLikeGeneratedCode_WithReportedGeneratedTrees_RaisesMs0013()
    {
        // The generated trees were reported, so the `.g.cs` naming convention
        // is not consulted: this file is still user code.
        var tree = CSharpSyntaxTree.ParseText(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            [Ignore]
            public class Marker {}

            public class Consumer
            {
                public void Use(Marker marker) {}
            }
            """,
            new CSharpParseOptions(LanguageVersion.Preview),
            path: "src/Consumer.g.cs"
        );

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [tree],
            TranspileHelper.BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(compilation, generatedTrees: []);

        await Assert.That(IgnoreReferenceDiagnostics(ir.Diagnostics)).IsNotEmpty();
    }

    private static string WorkspaceTextProgram(string workspaceTextDeclarations) =>
        $"""
            {JsonUsings}
            [assembly: TranspileAssembly]
            {WorkspaceTextContracts}
            {workspaceTextDeclarations}
            """;

    private static List<MetanoDiagnostic> IgnoreReferenceDiagnostics(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) =>
        diagnostics.Where(d => d.Code == DiagnosticCodes.IgnoreReferencedByTranspiledCode).ToList();
}
