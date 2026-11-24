using Metano.Annotations;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Frontend.Roslyn;

/// <summary>
/// Raises <c>MS0038</c> for every C# 15 union type the run would transpile, until
/// the union slices of spec 010 land (FR-012a, decision W7-2). Without it, a union
/// takes the struct path and is emitted as a wrapper class that stores nothing,
/// a silent wrong value. <see cref="RoslynTypeQueries.IsUnionType"/> answers for a
/// <c>union</c> declaration and for a hand-written <c>[Union]</c> type alike, so
/// both are rejected. Unions that are only used by transpiled code are reported by
/// the same <see cref="UnsupportedUnionRule"/> at their use sites. The error stops
/// the run before any output is written.
/// </summary>
internal sealed class UnionDeclarationValidator
{
    private readonly IAssemblySymbol _assembly;
    private readonly bool _assemblyWideTranspile;
    private readonly TargetLanguage _target;
    private readonly UnsupportedUnionRule _rule;

    private UnionDeclarationValidator(
        IAssemblySymbol assembly,
        bool assemblyWideTranspile,
        TargetLanguage target,
        UnsupportedUnionRule rule
    )
    {
        _assembly = assembly;
        _assemblyWideTranspile = assemblyWideTranspile;
        _target = target;
        _rule = rule;
    }

    public static void Validate(
        Compilation compilation,
        bool assemblyWideTranspile,
        TargetLanguage target,
        UnsupportedUnionRule rule
    )
    {
        var validator = new UnionDeclarationValidator(
            compilation.Assembly,
            assemblyWideTranspile,
            target,
            rule
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

        if (RoslynTypeQueries.IsUnionType(type))
            _rule.ReportDeclaration(type);

        foreach (var nested in type.GetTypeMembers())
            ScanType(nested);
    }
}
