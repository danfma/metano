using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The positions where a reference to an <c>[Ignore]</c> (.NET-only) type does NOT
/// raise <c>MS0013</c> (#247): source-generator output, an <c>[Ignore]</c> interface in
/// a transpilable type's base list (erased from <c>implements</c>/<c>extends</c>), and
/// attribute applications (attributes are never emitted). Every other position keeps
/// raising — see <see cref="IgnoreDotNetOnlyTests"/> plus the negative cases below.
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

    [Test]
    public async Task BrandedValueObject_WithIgnoreInterfaceAndConverterAttribute_DoesNotRaiseMs0013()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $"""
            {JsonUsings}
            [assembly: TranspileAssembly]
            {WorkspaceTextContracts}
            {AnnotatedWorkspaceText}
            """
        );

        await Assert.That(Ms0013(diagnostics)).IsEmpty();
        await Assert.That(files).ContainsKey("studio/workspace-text.ts");
    }

    [Test]
    public async Task BrandedValueObject_WithIgnoreInterfaceAndConverterAttribute_EmitsUnchangedTypeScript()
    {
        var annotated = TranspileHelper.Transpile(
            $"""
            {JsonUsings}
            [assembly: TranspileAssembly]
            {WorkspaceTextContracts}
            {AnnotatedWorkspaceText}
            """
        );
        var plain = TranspileHelper.Transpile(
            $"""
            {JsonUsings}
            [assembly: TranspileAssembly]
            {WorkspaceTextContracts}
            {PlainWorkspaceText}
            """
        );

        await Assert
            .That(annotated["studio/workspace-text.ts"])
            .IsEqualTo(plain["studio/workspace-text.ts"]);
    }

    [Test]
    public async Task IgnoreInterface_OnTranspilableClass_IsErasedFromImplements()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App;

            [Ignore]
            public interface IDotNetOnly
            {
                string Name { get; }
            }

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
        await Assert.That(Ms0013(diagnostics)).IsEmpty();
        await Assert.That(ts).Contains("implements IShared");
        await Assert.That(ts).DoesNotContain("IDotNetOnly");
    }

    [Test]
    public async Task IgnoreInterface_AsOnlyBaseOfTranspilableClass_DropsImplementsClause()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App;

            [Ignore]
            public interface IDotNetOnly
            {
                string Name { get; }
            }

            [Transpile]
            public class Person : IDotNetOnly
            {
                public string Name { get; } = "";
            }
            """
        );

        var ts = files["app/person.ts"];
        await Assert.That(Ms0013(diagnostics)).IsEmpty();
        await Assert.That(ts).DoesNotContain("implements");
        await Assert.That(ts).DoesNotContain("IDotNetOnly");
    }

    [Test]
    public async Task IgnoreInterface_InTranspilableInterfaceBaseList_IsErasedFromExtends()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace App;

            [Ignore]
            public interface IDotNetOnly
            {
                string Name { get; }
            }

            [Transpile]
            public interface IPerson : IDotNetOnly
            {
                int Age { get; }
            }
            """
        );

        var ts = files["app/i-person.ts"];
        await Assert.That(Ms0013(diagnostics)).IsEmpty();
        await Assert.That(ts).DoesNotContain("extends");
        await Assert.That(ts).DoesNotContain("IDotNetOnly");
    }

    [Test]
    public async Task IgnoreInterface_OnTranspilableClass_IsErasedFromDartImplements()
    {
        var (files, _) = TranspileHelper.TranspileDart(
            """
            using Metano.Annotations;

            [Ignore]
            public interface IDotNetOnly
            {
                string Name { get; }
            }

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
        await Assert.That(dart).Contains("implements IShared");
        await Assert.That(dart).DoesNotContain("IDotNetOnly");
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

        await Assert.That(Ms0013(diagnostics)).IsEmpty();
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

        await Assert.That(Ms0013(diagnostics)).IsNotEmpty();
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

        await Assert.That(Ms0013(diagnostics)).IsNotEmpty();
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

        await Assert.That(Ms0013(diagnostics)).IsNotEmpty();
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

        await Assert.That(Ms0013(diagnostics)).IsNotEmpty();
    }

    [Test]
    public async Task SourceGeneratedJsonContext_ReferencingIgnoreConverter_DoesNotRaiseMs0013()
    {
        // The property-level [JsonConverter] is exempt (attribute), but the real
        // System.Text.Json generator emits `new UpperCaseConverter()` into the
        // context's generated partial — not user-authored, so not scanned.
        var (_, diagnostics) = TranspileHelper.TranspileWithJsonSourceGenerator(
            """
            using System.Text.Json;
            using System.Text.Json.Serialization;
            [assembly: TranspileAssembly]

            namespace Studio;

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

            public sealed class Document
            {
                [JsonConverter(typeof(UpperCaseConverter))]
                public string Title { get; set; } = "";
            }

            [JsonSerializable(typeof(Document))]
            public partial class StudioJsonContext : JsonSerializerContext;
            """
        );

        await Assert.That(Ms0013(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task SourceGeneratedJsonContext_WithBrandedWorkspaceText_DoesNotRaiseMs0013()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithJsonSourceGenerator(
            $"""
            {JsonUsings}
            [assembly: TranspileAssembly]
            {WorkspaceTextContracts}
            {AnnotatedWorkspaceText}

            public sealed record Document(WorkspaceText Title);

            [JsonSerializable(typeof(Document))]
            public partial class StudioJsonContext : JsonSerializerContext;
            """
        );

        await Assert.That(Ms0013(diagnostics)).IsEmpty();
    }

    private static List<MetanoDiagnostic> Ms0013(IEnumerable<MetanoDiagnostic> diagnostics) =>
        diagnostics.Where(d => d.Code == DiagnosticCodes.IgnoreReferencedByTranspiledCode).ToList();
}
