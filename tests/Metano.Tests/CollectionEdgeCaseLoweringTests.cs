using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Wave 6 group A (#712): the <c>ImmutableArray&lt;T&gt;</c> members whose runtime counterpart is a
/// renamed property.
/// </summary>
public class CollectionEdgeCaseLoweringTests
{
    private const string Host = """
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using Metano.Annotations;

        [Transpile]
        public class Calls
        {
        """;

    [Test]
    public async Task ImmutableArrayIsDefaultOrEmpty_IsIsEmpty()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Returning("ImmutableArray<int> xs", "xs.IsDefaultOrEmpty")
        );

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
        await Assert.That(files["calls.ts"]).Contains("return xs.isEmpty;");
    }

    private static string Returning(string parameters, string expression) =>
        Host
        + $$"""
                public object Run({{parameters}}) => {{expression}};
            }
            """;

    private static IEnumerable<MetanoDiagnostic> UnsupportedFeatures(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCodes.UnsupportedFeature);
}
