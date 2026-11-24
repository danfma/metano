using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Frontend.Roslyn;

/// <summary>
/// Raises <c>MS0038</c> for every C# 15 union type the run would transpile, until
/// the union slices of spec 010 land (FR-012a, decision W7-2). Without it, a union
/// takes the struct path and is emitted as a wrapper class that stores nothing,
/// a silent wrong value. Roslyn's <see cref="ITypeSymbol.IsUnion"/> answers for a
/// <c>union</c> declaration and for a hand-written <c>[Union]</c> type alike, so
/// both are rejected. The error stops the run before any output is written.
/// </summary>
internal sealed class UnionDeclarationValidator
{
    private readonly IAssemblySymbol _assembly;
    private readonly bool _assemblyWideTranspile;
    private readonly TargetLanguage _target;
    private readonly List<MetanoDiagnostic> _diagnostics;

    private UnionDeclarationValidator(
        IAssemblySymbol assembly,
        bool assemblyWideTranspile,
        TargetLanguage target,
        List<MetanoDiagnostic> diagnostics
    )
    {
        _assembly = assembly;
        _assemblyWideTranspile = assemblyWideTranspile;
        _target = target;
        _diagnostics = diagnostics;
    }

    public static void Validate(
        Compilation compilation,
        bool assemblyWideTranspile,
        TargetLanguage target,
        List<MetanoDiagnostic> diagnostics
    )
    {
        var validator = new UnionDeclarationValidator(
            compilation.Assembly,
            assemblyWideTranspile,
            target,
            diagnostics
        );

        CSharpSourceFrontend.CollectTopLevelTypes(
            compilation.Assembly.GlobalNamespace,
            validator.ScanType
        );
    }

    private bool IsTranspiled(INamedTypeSymbol type) =>
        SymbolHelper.IsTranspilable(type, _assemblyWideTranspile, _assembly)
        && !SymbolHelper.HasIgnore(type, _target);

    private void ScanType(INamedTypeSymbol type)
    {
        if (!IsTranspiled(type))
            return;

        if (type.IsUnion)
            _diagnostics.Add(UnsupportedUnionDiagnostic(type));

        foreach (var nested in type.GetTypeMembers())
            ScanType(nested);
    }

    private static MetanoDiagnostic UnsupportedUnionDiagnostic(INamedTypeSymbol union) =>
        new(
            MetanoDiagnosticSeverity.Error,
            DiagnosticCodes.UnsupportedUnionShape,
            $"Union declaration '{union.Name}' is not supported by this Metano version; "
                + "C# 15 unions are planned (spec 010). Use a closed class hierarchy "
                + "instead, or keep the union out of the transpiled types.",
            union.Locations.FirstOrDefault()
        );
}
