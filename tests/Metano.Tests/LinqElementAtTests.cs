using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// <c>ElementAt</c>, <c>ElementAtOrDefault</c> and <c>DefaultIfEmpty</c> lower to the pipe
/// operators of <c>metano-runtime/system/linq</c> (#617), in the extension and the static form.
/// An <c>OrDefault</c> miss that is not <c>null</c> passes the default of the element type; the
/// Dart target keeps its own member calls.
/// </summary>
public class LinqElementAtTests
{
    [Test]
    [Arguments("xs.ElementAt(1)", "linq(xs, elementAt(1))")]
    [Arguments("Enumerable.ElementAt(xs, 1)", "linq(xs, elementAt(1))")]
    [Arguments(
        "xs.Where(x => x > 1).ElementAt(0)",
        "linq(xs, where((x: number) => x > 1), elementAt(0))"
    )]
    [Arguments("xs.ElementAtOrDefault(3)", "linq(xs, elementAtOrDefault(3, 0))")]
    [Arguments("ss.ElementAtOrDefault(3)", "linq(ss, elementAtOrDefault(3))")]
    [Arguments("ns.ElementAtOrDefault(3)", "linq(ns, elementAtOrDefault(3))")]
    [Arguments("Enumerable.ElementAtOrDefault(xs, 3)", "linq(xs, elementAtOrDefault(3, 0))")]
    public async Task ElementAt_LowersToThePipeTerminal(string call, string emitted)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Body(call));

        await Assert.That(Unsupported(diagnostics)).IsEmpty();
        await Assert.That(files["calls.ts"]).Contains($"return {emitted};");
    }

    [Test]
    public async Task ElementAtOfLong_PassesTheLongDefault()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(Body("ls.ElementAtOrDefault(1)"));

        await Assert
            .That(files["calls.ts"])
            .Contains("return linq(ls, elementAtOrDefault(1, 0n));");
    }

    [Test]
    [Arguments("xs.DefaultIfEmpty()", "linq(xs, defaultIfEmpty(0))")]
    [Arguments("xs.DefaultIfEmpty(7)", "linq(xs, defaultIfEmpty(7))")]
    [Arguments("ss.DefaultIfEmpty()", "linq(ss, defaultIfEmpty())")]
    [Arguments("ss.DefaultIfEmpty(\"x\")", "linq(ss, defaultIfEmpty(\"x\"))")]
    [Arguments("ns.DefaultIfEmpty()", "linq(ns, defaultIfEmpty())")]
    [Arguments("Enumerable.DefaultIfEmpty(xs)", "linq(xs, defaultIfEmpty(0))")]
    public async Task DefaultIfEmpty_LowersToThePipeOperator(string call, string emitted)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Body(call));

        await Assert.That(Unsupported(diagnostics)).IsEmpty();
        await Assert.That(files["calls.ts"]).Contains($"return {emitted};");
    }

    [Test]
    [Arguments("xs.ElementAt(^1)")]
    [Arguments("xs.Distinct(same).ElementAt(^1)")]
    public async Task ElementAtFromTheEnd_Reports(string call)
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Body(call));

        await Assert
            .That(Unsupported(diagnostics).Single().Message)
            .IsEqualTo(
                "The overload 'ElementAt(index: Index)' is not supported; the runtime implements only 'ElementAt(index)'. The call throws at run time."
            );
    }

    [Test]
    public async Task DistinctWithAComparer_PassesTheComparerToTheOperator()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Body("xs.Distinct(same)")
        );

        await Assert.That(Unsupported(diagnostics)).IsEmpty();
        await Assert.That(files["calls.ts"]).Contains("return linq(xs, distinct(same));");
    }

    [Test]
    public async Task Dart_KeepsTheMemberCalls()
    {
        var (files, diagnostics) = TranspileHelper.TranspileDart(
            "using Metano.Annotations;\n" + Body("xs.ElementAt(1)")
        );

        await Assert.That(Unsupported(diagnostics)).IsEmpty();
        await Assert.That(files["calls.dart"]).Contains("xs.elementAt(1)");
    }

    private static IEnumerable<MetanoDiagnostic> Unsupported(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCodes.UnsupportedFeature);

    private static string Body(string expression) =>
        $$"""
            using System;
            using System.Collections.Generic;
            using System.Linq;

            [Transpile]
            public class Calls
            {
                public object Run(
                    List<int> xs,
                    List<string> ss,
                    List<int?> ns,
                    List<long> ls,
                    IEqualityComparer<int> same
                ) => {{expression}};
            }
            """;
}
