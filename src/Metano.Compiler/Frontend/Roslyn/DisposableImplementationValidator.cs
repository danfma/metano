using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Frontend.Roslyn;

/// <summary>
/// Reports, as <c>MS0001</c> errors, a transpiled type whose emitted shape cannot satisfy
/// the runtime <c>IDisposable</c> interface it now implements (FR-111): a
/// <c>[PlainObject]</c>, whose <c>Dispose</c> lowers to a helper function rather than a
/// member, and a type whose own implementing <c>Dispose</c> is <c>[Ignore]</c> or
/// <c>[Emit]</c> (inlined at call sites, so neither <c>dispose()</c> nor the
/// <c>[Symbol.dispose]()</c> alias that calls it exists). Only for a
/// target that declares <c>IDisposable</c>; generated sources are not policed.
/// </summary>
internal sealed class DisposableImplementationValidator
{
    private readonly IAssemblySymbol _assembly;
    private readonly bool _assemblyWideTranspile;
    private readonly TargetLanguage _target;
    private readonly GeneratedSourceTrees _generatedSources;
    private readonly IMethodSymbol _interfaceDispose;
    private readonly List<MetanoDiagnostic> _diagnostics;

    private DisposableImplementationValidator(
        Compilation compilation,
        bool assemblyWideTranspile,
        TargetLanguage target,
        GeneratedSourceTrees generatedSources,
        List<MetanoDiagnostic> diagnostics
    )
    {
        _assembly = compilation.Assembly;
        _assemblyWideTranspile = assemblyWideTranspile;
        _target = target;
        _generatedSources = generatedSources;
        _diagnostics = diagnostics;

        _interfaceDispose = compilation
            .GetSpecialType(SpecialType.System_IDisposable)
            .GetMembers(nameof(IDisposable.Dispose))
            .OfType<IMethodSymbol>()
            .First();
    }

    public static void Validate(
        Compilation compilation,
        bool assemblyWideTranspile,
        TargetLanguage target,
        GeneratedSourceTrees generatedSources,
        List<MetanoDiagnostic> diagnostics
    )
    {
        if (!DisposableInterfaces.IsDeclaredBy(target))
            return;

        var validator = new DisposableImplementationValidator(
            compilation,
            assemblyWideTranspile,
            target,
            generatedSources,
            diagnostics
        );

        CSharpSourceFrontend.CollectTopLevelTypes(
            compilation.Assembly.GlobalNamespace,
            validator.Scan
        );
    }

    private void Scan(INamedTypeSymbol type)
    {
        if (!IsPolicedType(type))
            return;

        if (type.Interfaces.Any(DisposableInterfaces.Is) && ReasonOf(type) is { } reason)
            _diagnostics.Add(Unsatisfiable(type, reason));

        foreach (var nested in type.GetTypeMembers())
            Scan(nested);
    }

    private bool IsPolicedType(INamedTypeSymbol type) =>
        SymbolHelper.IsTranspilable(type, _assemblyWideTranspile, _assembly)
        && !SymbolHelper.HasIgnore(type, _target)
        && !_generatedSources.DeclaresOnlyGeneratedCode(type);

    private string? ReasonOf(INamedTypeSymbol type)
    {
        if (type.HasPlainObject())
            return "it is a [PlainObject], whose Dispose lowers to a helper function rather "
                + "than a member";

        return type.FindImplementationForInterfaceMember(_interfaceDispose) switch
        {
            { } dispose when dispose.HasIgnore(_target) =>
                $"its implementing '{dispose.Name}' is [Ignore]",
            { } dispose when dispose.HasEmit() =>
                $"its implementing '{dispose.Name}' is [Emit], which inlines its calls "
                    + "instead of declaring a member",
            _ => null,
        };
    }

    private static MetanoDiagnostic Unsatisfiable(INamedTypeSymbol type, string reason) =>
        new(
            MetanoDiagnosticSeverity.Error,
            DiagnosticCodes.UnsupportedFeature,
            $"'{type.Name}' implements IDisposable, which the target declares as an interface "
                + $"with a 'dispose()' member, but {reason}, so the emitted type would not "
                + "satisfy it (FR-111).",
            type.Locations.FirstOrDefault()
        );
}
