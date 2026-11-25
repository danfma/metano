using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Frontend.Roslyn;

/// <summary>
/// <c>MS0002</c> (#770): a reference from emitted code to a type the output never
/// declares or imports, which would fail with a ReferenceError at run time:
/// <list type="bullet">
///   <item>under <c>[assembly: TranspileAssembly]</c>, a type of the same assembly that is
///   still not emitted after <see cref="InternalTypeReachability"/> pulled in every
///   reachable internal type (a <c>file</c>-local type today);</item>
///   <item>in any mode, a non-public type of a referenced Metano package, reached through
///   <c>[InternalsVisibleTo]</c>: the package emits it but never exports it, so a
///   referencing package cannot import it.</item>
/// </list>
/// <c>[Ignore]</c> keeps its own <c>MS0013</c>, and <c>[External]</c> /
/// <c>[JsCallable]</c> types are ambient by design.
/// </summary>
internal sealed class UnemittedTypeReferenceRule(
    IAssemblySymbol assembly,
    bool assemblyWideTranspile,
    TargetLanguage target,
    List<MetanoDiagnostic> diagnostics
) : ITranspiledReferenceRule
{
    private readonly IAssemblySymbol _assembly = assembly;
    private readonly bool _assemblyWideTranspile = assemblyWideTranspile;
    private readonly TargetLanguage _target = target;
    private readonly List<MetanoDiagnostic> _diagnostics = diagnostics;

    /// <summary>
    /// Where a reference ends: <c>Lib.Codec</c> (the receiver of a static access) and its
    /// last name <c>Codec</c> are one reference, reported once.
    /// </summary>
    private readonly HashSet<(SyntaxTree?, int)> _reportedReferences = [];

    public static UnemittedTypeReferenceRule For(
        Compilation compilation,
        bool assemblyWideTranspile,
        TargetLanguage target,
        List<MetanoDiagnostic> diagnostics
    ) => new(compilation.Assembly, assemblyWideTranspile, target, diagnostics);

    public void CheckType(INamedTypeSymbol type, Location location)
    {
        if (
            IsUnemitted(type.OriginalDefinition)
            && SpellsTheType(location)
            && _reportedReferences.Add((location.SourceTree, location.SourceSpan.End))
        )
            Report(type, location);
    }

    public void CheckMember(ISymbol member, Location location) { }

    /// <summary>
    /// Whether the output prints the type at <paramref name="location"/>: a name that spells
    /// it, a target-typed <c>new()</c>, or a declaration whose signature carries it. The
    /// receiver of an instance access (<c>Make().V</c>, <c>api.Internal().V</c>) and a
    /// <c>var</c> keyword print no type, so a value of the type flowing through them is
    /// not a missing symbol.
    /// </summary>
    private static bool SpellsTheType(Location location)
    {
        if (location.SourceTree is not { } tree)
            return true;

        var node = tree.GetRoot().FindNode(location.SourceSpan, getInnermostNodeForTie: true);

        return node switch
        {
            IdentifierNameSyntax { IsVar: true } => false,
            NameSyntax => true,
            ImplicitObjectCreationExpressionSyntax => true,
            ExpressionSyntax => false,
            _ => true,
        };
    }

    private bool IsUnemitted(INamedTypeSymbol type)
    {
        var isUserType =
            type is { IsImplicitlyDeclared: false, IsAnonymousType: false }
            && !string.IsNullOrEmpty(type.Name)
            && !SymbolHelper.HasIgnore(type, _target)
            && !SymbolHelper.HasExternal(type)
            && !SymbolHelper.HasJsCallable(type);

        if (!isUserType)
            return false;

        return SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, _assembly)
            ? IsUnemittedOwnType(type)
            : IsUnexportedPackageType(type);
    }

    private bool IsUnemittedOwnType(INamedTypeSymbol type) =>
        _assemblyWideTranspile
        && type.Locations.Any(location => location.IsInSource)
        && !SymbolHelper.IsTranspilable(type, assemblyWideTranspile: true, _assembly);

    /// <summary>
    /// A type of a referenced Metano package that is not public (itself or a container):
    /// the package keeps it out of its barrels and exports, so it cannot be imported.
    /// </summary>
    private static bool IsUnexportedPackageType(INamedTypeSymbol type) =>
        type.ContainingAssembly is { } package
        && (package.HasTranspileAssembly() || SymbolHelper.HasTranspile(type))
        && !IsEffectivelyPublic(type);

    private static bool IsEffectivelyPublic(INamedTypeSymbol type)
    {
        for (
            INamedTypeSymbol? current = type;
            current is not null;
            current = current.ContainingType
        )
        {
            if (current.DeclaredAccessibility != Accessibility.Public)
                return false;
        }

        return true;
    }

    private void Report(INamedTypeSymbol type, Location location) =>
        _diagnostics.Add(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnresolvedType,
                $"Transpiled code references '{type.Name}', which the output never declares "
                    + "or imports (a file-local type, or an internal type of another package). "
                    + "Make the type transpilable and public, or remove the reference.",
                location
            )
        );
}
