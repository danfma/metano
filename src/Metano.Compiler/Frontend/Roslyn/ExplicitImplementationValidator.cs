using System.Diagnostics;
using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Frontend.Roslyn;

/// <summary>
/// Raises <c>MS0031</c> for every explicit interface implementation in a transpilable
/// type that <see cref="ExplicitImplementationLowering"/> cannot declare under its
/// interface member's name because another member already claims it. The class
/// extractor drops those members, so the diagnostic is the only trace they leave.
/// Members a source generator added (the System.Text.Json context's
/// <c>IJsonTypeInfoResolver.GetTypeInfo</c>, for instance) are not user-authored and
/// are not policed, matching <see cref="IgnoreReferenceValidator"/>.
/// </summary>
internal sealed class ExplicitImplementationValidator
{
    private readonly IAssemblySymbol _assembly;
    private readonly bool _assemblyWideTranspile;
    private readonly TargetLanguage _target;
    private readonly GeneratedSourceTrees _generatedSources;
    private readonly List<MetanoDiagnostic> _diagnostics;

    private ExplicitImplementationValidator(
        IAssemblySymbol assembly,
        bool assemblyWideTranspile,
        TargetLanguage target,
        GeneratedSourceTrees generatedSources,
        List<MetanoDiagnostic> diagnostics
    )
    {
        _assembly = assembly;
        _assemblyWideTranspile = assemblyWideTranspile;
        _target = target;
        _generatedSources = generatedSources;
        _diagnostics = diagnostics;
    }

    public static void Validate(
        Compilation compilation,
        bool assemblyWideTranspile,
        TargetLanguage target,
        GeneratedSourceTrees generatedSources,
        List<MetanoDiagnostic> diagnostics
    )
    {
        var validator = new ExplicitImplementationValidator(
            compilation.Assembly,
            assemblyWideTranspile,
            target,
            generatedSources,
            diagnostics
        );

        CSharpSourceFrontend.CollectTopLevelTypes(
            compilation.Assembly.GlobalNamespace,
            type =>
            {
                if (validator.IsPolicedType(type))
                    validator.ScanType(type);
            }
        );
    }

    /// <summary>
    /// A transpilable, user-authored type that is not itself ignored for the target.
    /// Nested types follow the same rule: under <c>[TranspileAssembly]</c> they are
    /// emitted without their own <c>[Transpile]</c> (#264).
    /// </summary>
    private bool IsPolicedType(INamedTypeSymbol type) =>
        SymbolHelper.IsTranspilable(type, _assemblyWideTranspile, _assembly)
        && !SymbolHelper.HasIgnore(type, _target)
        && !_generatedSources.DeclaresOnlyGeneratedCode(type);

    private bool IsPolicedMember(ISymbol member) =>
        !SymbolHelper.HasIgnore(member, _target)
        && !_generatedSources.DeclaresOnlyGeneratedCode(member);

    private void ScanType(INamedTypeSymbol type)
    {
        var conflicts = type.GetMembers()
            .Where(IsPolicedMember)
            .Select(member => ExplicitImplementationLowering.Resolve(member, _target))
            .OfType<ExplicitImplementation>()
            .Where(lowering => lowering.Fate is ExplicitImplementationFate.Conflicting);

        foreach (var conflict in conflicts)
            _diagnostics.Add(ConflictDiagnostic(conflict));

        foreach (var nested in type.GetTypeMembers())
        {
            if (IsPolicedType(nested))
                ScanType(nested);
        }
    }

    private static MetanoDiagnostic ConflictDiagnostic(ExplicitImplementation conflict)
    {
        var member = conflict.Member;
        var interfaceMember = conflict.InterfaceMember;
        var interfaceName = interfaceMember.ContainingType.ToDisplayString(
            SymbolDisplayFormat.MinimallyQualifiedFormat
        );
        var conflictingMember = conflict.ClaimingMember ?? throw new UnreachableException();

        return new MetanoDiagnostic(
            MetanoDiagnosticSeverity.Error,
            DiagnosticCodes.ExplicitImplementationConflict,
            $"Explicit implementation '{interfaceName}.{interfaceMember.Name}' on "
                + $"'{member.ContainingType.Name}' cannot be emitted: the target has no "
                + "explicit interface implementations, so it would be declared as "
                + $"'{interfaceMember.Name}', which '{conflictingMember.ContainingType.Name}."
                + $"{conflictingMember.Name}' already uses. Rename one of them, implement the "
                + "interface member implicitly, or mark the explicit member [Ignore].",
            member.Locations.FirstOrDefault()
        );
    }
}
