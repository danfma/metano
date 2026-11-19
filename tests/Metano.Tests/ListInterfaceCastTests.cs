using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Spec 011 contracts/runtime-collections.md § Helpers: a cast to a list
/// interface from a static type that may hold a native array at run time
/// (<c>object</c>, <c>IEnumerable&lt;T&gt;</c>) lowers to the runtime
/// <c>asList</c>, typed as the cast's target; the cast from an array keeps
/// <c>List.adopt</c> and the cast from a collection stays a plain cast.
/// </summary>
public class ListInterfaceCastTests
{
    [Test]
    [Arguments(
        "object value",
        "(IList<int>)value",
        "asList<number>(value, \"System.Collections.Generic.IList`1[System.Int32]\") as IList<number>"
    )]
    [Arguments(
        "IEnumerable<int> value",
        "(IReadOnlyList<int>)value",
        "asList<number>(value, \"System.Collections.Generic.IReadOnlyList`1[System.Int32]\") as IReadOnlyList<number>"
    )]
    [Arguments(
        "List<int> value",
        "(ICollection<int>)(object)value",
        "asList<number>(value, \"System.Collections.Generic.ICollection`1[System.Int32]\") as ICollection<number>"
    )]
    [Arguments(
        "int[] value",
        "(IReadOnlyCollection<int>)(object)value",
        "asList<number>(value, \"System.Collections.Generic.IReadOnlyCollection`1[System.Int32]\") as IReadOnlyCollection<number>"
    )]
    public async Task CastFromAPossibleArray_LowersToAsList(
        string parameter,
        string cast,
        string expected
    )
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Returning(parameter, cast)
        );

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
        await Assert.That(files["casts.ts"]).Contains($"return {expected};");
        await Assert.That(files["casts.ts"]).Contains("asList");
        await Assert.That(files["casts.ts"]).Contains("from \"metano-runtime/system/collections\"");
    }

    [Test]
    public async Task CastFromAnArray_AdoptsIt()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(
            Returning("int[] value", "(IList<int>)value")
        );

        await Assert
            .That(files["casts.ts"])
            .Contains("return List.adopt<number>(value, { fixedSize: true });");
    }

    [Test]
    public async Task AsFromObject_StillReportsMs0001()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Returning("object value", "value as IList<int>")
        );

        await Assert.That(UnsupportedFeatures(diagnostics)).IsNotEmpty();
    }

    private static string Returning(string parameter, string expression) =>
        $$"""
            using System.Collections.Generic;
            using Metano.Annotations;

            [Transpile]
            public static class Casts
            {
                public static IEnumerable<int>? View({{parameter}}) => {{expression}};
            }
            """;

    private static IEnumerable<MetanoDiagnostic> UnsupportedFeatures(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCodes.UnsupportedFeature);
}
