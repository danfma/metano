using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Diagnostics for C# indexers, which a TypeScript class or interface cannot declare
/// (<c>this[int i]</c> has no member form): the indexer is left out (#474).
/// </summary>
internal static class IndexerDiagnostics
{
    /// <summary>
    /// Reports MS0001 (warning) when <paramref name="ir"/> declares an indexer: it is
    /// not emitted, as on 3.0.0 for a <c>[PlainObject]</c>, so the type still compiles
    /// as long as nothing indexes it.
    /// </summary>
    public static void Report(
        INamedTypeSymbol type,
        IrClassDeclaration ir,
        Action<MetanoDiagnostic> report
    )
    {
        var declaresIndexer = (ir.Members ?? []).Any(member =>
            member is IrPropertyDeclaration { Semantics.IsIndexer: true }
        );

        if (!declaresIndexer)
            return;

        var indexer = type.GetMembers()
            .OfType<IPropertySymbol>()
            .FirstOrDefault(property => property.IsIndexer);

        report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                $"Indexers are not supported in TypeScript: a class member cannot take an "
                    + $"index operand, so the indexer of '{type.Name}' is not emitted.",
                (indexer ?? (ISymbol)type).Locations.FirstOrDefault()
            )
        );
    }
}
