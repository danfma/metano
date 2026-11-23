using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The members of a primitive-backed <c>[Branded]</c> type that its TypeScript
/// lowering does not declare (issues #623, #634): the brand is the primitive
/// itself and the companion object carries only the factory, the static
/// methods and the conversions, so an instance method, a property other than
/// the wrapped value, and a static field or property have nowhere to live.
/// Each one reports <c>MS0001</c> at its declaration instead of vanishing and
/// leaving its call sites to fail at run time. An override (<c>ToString</c>),
/// an <c>[Emit]</c> template, which the call site inlines, and an
/// <c>[Ignore]</c>d member are not reported.
/// </summary>
internal static class BrandedUnemittedMembers
{
    public static IReadOnlyList<TsClaimFinding> FindingsOf(INamedTypeSymbol type)
    {
        var valueMember = SymbolHelper.GetBrandedValueMember(type.OriginalDefinition);

        return type
            .OriginalDefinition.GetMembers()
            .Where(member => !SymbolEqualityComparer.Default.Equals(member, valueMember))
            .Where(IsUnemitted)
            .Select(member => FindingOf(type, member))
            .ToList();
    }

    private static bool IsUnemitted(ISymbol member) =>
        !member.IsImplicitlyDeclared
        && !member.HasIgnore(TargetLanguage.TypeScript)
        && !member.HasEmit()
        && member switch
        {
            IMethodSymbol method => method
                is { MethodKind: MethodKind.Ordinary, IsStatic: false, IsOverride: false },
            IPropertySymbol or IFieldSymbol => true,
            _ => false,
        };

    private static TsClaimFinding FindingOf(INamedTypeSymbol type, ISymbol member) =>
        new(
            MetanoDiagnosticSeverity.Warning,
            DiagnosticCodes.UnsupportedFeature,
            $"'{type.Name}.{member.Name}' is not emitted: the [Branded] type '{type.Name}' "
                + "lowers to its wrapped primitive plus a companion object that declares only "
                + "its factory, static methods and conversions, so its "
                + $"{KindOf(member)} has no TypeScript counterpart. Make it a static method "
                + "that takes the value, or mark it [Ignore].",
            new OverloadDeclarationSite(member.Name)
        );

    private static string KindOf(ISymbol member) =>
        member switch
        {
            IMethodSymbol => "instance method",
            IPropertySymbol { IsStatic: true } => "static property",
            IPropertySymbol => "instance property",
            IFieldSymbol { IsConst: true } => "constant",
            IFieldSymbol { IsStatic: true } => "static field",
            _ => "instance field",
        };
}
